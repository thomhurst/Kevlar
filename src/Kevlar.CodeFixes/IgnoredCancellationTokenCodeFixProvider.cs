using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Kevlar.CodeFixes;

/// <summary>Forwards an unused execution token to a semantically compatible invocation.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IgnoredCancellationTokenCodeFixProvider)), Shared]
public sealed class IgnoredCancellationTokenCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "ForwardExecutionCancellationToken";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("KEV001");

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null) { return; }

        var lambda = root.FindNode(context.Span, getInnermostNodeForTie: true)
            .FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>();
        if (lambda is null
            || model.GetOperation(lambda, context.CancellationToken) is not IAnonymousFunctionOperation operation)
        {
            return;
        }

        var tokenType = model.Compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        var token = operation.Symbol.Parameters.LastOrDefault(parameter =>
            SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType));
        var invocation = GetInvocation(lambda.Body);
        if (token is null || invocation is null
            || model.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation original
            || model.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol originalMethod
            || original.Arguments.Any(argument => !argument.IsImplicit
                && SymbolEqualityComparer.Default.Equals(argument.Parameter?.Type, tokenType)))
        {
            return;
        }

        // GetMemberGroup includes overloads; speculative binding selects the accessible, unambiguous one.
        var names = model.GetMemberGroup(invocation.Expression, context.CancellationToken)
            .OfType<IMethodSymbol>().Append(originalMethod)
            .SelectMany(method => method.Parameters)
            .Where(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType))
            .Select(parameter => parameter.Name).Distinct(StringComparer.Ordinal);
        InvocationExpressionSyntax? replacement = null;
        foreach (var name in names)
        {
            var argument = SyntaxFactory.Argument(Identifier(token.Name))
                .WithNameColon(SyntaxFactory.NameColon(Identifier(name))
                    .WithColonToken(SyntaxFactory.Token(SyntaxKind.ColonToken).WithTrailingTrivia(SyntaxFactory.Space)));
            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                argument = argument.WithLeadingTrivia(SyntaxFactory.Space);
            }
            var candidate = invocation.WithArgumentList(invocation.ArgumentList.AddArguments(argument));
            var binding = model.GetSpeculativeSymbolInfo(invocation.SpanStart, candidate, SpeculativeBindingOption.BindAsExpression);
            if (binding.Symbol is not IMethodSymbol selected
                || !PreservesSignature(originalMethod, selected, tokenType!))
            {
                continue;
            }

            // Also preserve the enclosing execution overload and its delegate conversion.
            var execution = lambda.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (execution is null
                || model.GetSymbolInfo(execution, context.CancellationToken).Symbol is not IMethodSymbol executionMethod
                || !SymbolEqualityComparer.Default.Equals(executionMethod,
                    model.GetSpeculativeSymbolInfo(execution.SpanStart, execution.ReplaceNode(invocation, candidate),
                        SpeculativeBindingOption.BindAsExpression).Symbol))
            {
                continue;
            }

            if (replacement is not null) { return; }
            replacement = candidate;
        }

        if (replacement is null) { return; }
        var changedDocument = context.Document.WithSyntaxRoot(root.ReplaceNode(invocation, replacement));
        var changedModel = await changedDocument.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (changedModel is null || changedModel.GetDiagnostics(cancellationToken: context.CancellationToken)
            .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }
        context.RegisterCodeFix(CodeAction.Create("Forward execution CancellationToken",
            _ => Task.FromResult(changedDocument), EquivalenceKey), context.Diagnostics);
    }

    private static InvocationExpressionSyntax? GetInvocation(CSharpSyntaxNode body)
    {
        if (body is BlockSyntax { Statements.Count: 1 } block)
        {
            body = block.Statements[0] switch
            {
                ReturnStatementSyntax { Expression: { } expression } => expression,
                ExpressionStatementSyntax statement => statement.Expression,
                _ => body,
            };
        }
        if (body is AwaitExpressionSyntax awaited) { body = awaited.Expression; }
        return body as InvocationExpressionSyntax;
    }

    private static IdentifierNameSyntax Identifier(string name) => SyntaxFactory.IdentifierName(SyntaxFactory.ParseToken(
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
            ? "@" + name : name));

    private static bool PreservesSignature(IMethodSymbol original, IMethodSymbol selected, ITypeSymbol tokenType)
    {
        if (original.Name != selected.Name || original.IsStatic != selected.IsStatic
            || original.MethodKind != selected.MethodKind || original.RefKind != selected.RefKind
            || !SymbolEqualityComparer.Default.Equals(original.ContainingType, selected.ContainingType)
            || !SymbolEqualityComparer.Default.Equals(original.ReturnType, selected.ReturnType)
            || !original.TypeArguments.AsEnumerable().SequenceEqual(selected.TypeArguments, SymbolEqualityComparer.Default))
        {
            return false;
        }

        var tokenParameters = selected.Parameters.Where(parameter =>
            SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType)).ToArray();
        if (tokenParameters.Length != 1 || tokenParameters[0].RefKind != RefKind.None) { return false; }

        var oldParameters = original.Parameters.Where(parameter =>
            !SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType)).ToArray();
        var newParameters = selected.Parameters.Where(parameter =>
            !SymbolEqualityComparer.Default.Equals(parameter.Type, tokenType)).ToArray();
        if (oldParameters.Length != newParameters.Length) { return false; }
        for (var index = 0; index < oldParameters.Length; index++)
        {
            var before = oldParameters[index];
            var after = newParameters[index];
            if (before.Name != after.Name || before.RefKind != after.RefKind || before.IsParams != after.IsParams
                || before.IsOptional != after.IsOptional
                || before.HasExplicitDefaultValue != after.HasExplicitDefaultValue
                || (before.HasExplicitDefaultValue && !Equals(before.ExplicitDefaultValue, after.ExplicitDefaultValue))
                || !SymbolEqualityComparer.Default.Equals(before.Type, after.Type)
                || !before.GetAttributes().Select(attribute => attribute.ToString())
                    .SequenceEqual(after.GetAttributes().Select(attribute => attribute.ToString()), StringComparer.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}
