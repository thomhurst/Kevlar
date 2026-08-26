using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kevlar.Analyzers;

/// <summary>Moves asynchronous lambdas from synchronous strategy hooks to their async twin.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncCallbackCodeFixProvider)), Shared]
internal sealed class AsyncCallbackCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use the asynchronous callback";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("KEV013");

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        var assignment = root?
            .FindNode(context.Span)
            .FirstAncestorOrSelf<AssignmentExpressionSyntax>();
        if (assignment?.Right is not AnonymousFunctionExpressionSyntax callback)
        {
            return;
        }

        if (assignment.IsKind(SyntaxKind.AddAssignmentExpression)
            || assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            return;
        }

        if (callback.AsyncKeyword.IsKind(SyntaxKind.None)
            && callback.Body is BlockSyntax)
        {
            return;
        }

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        var property = semanticModel?.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol
            as IPropertySymbol;
        if (property is null)
        {
            return;
        }

        if (callback.Body is BlockSyntax block
            && ContainsUnawaitedTaskInvocation(block, semanticModel!, context.CancellationToken))
        {
            return;
        }

        if (callback is LambdaExpressionSyntax
            {
                AsyncKeyword.RawKind: 0,
                ExpressionBody: { } expressionBody,
            }
            && !HasCompatibleResult(semanticModel!.GetTypeInfo(
                expressionBody,
                context.CancellationToken).Type))
        {
            return;
        }

        var asyncProperty = property.ContainingType.GetMembers(property.Name + "Async")
            .OfType<IPropertySymbol>()
            .FirstOrDefault();
        if (asyncProperty is null
            || IsAlreadyAssigned(assignment, asyncProperty, semanticModel!, context.CancellationToken))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                Title,
                cancellationToken => RenamePropertyAsync(
                    context.Document,
                    assignment,
                    asyncProperty.Name,
                    cancellationToken),
                Title),
            context.Diagnostics);
    }

    private static bool ContainsUnawaitedTaskInvocation(
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var pendingBodies = new Stack<SyntaxNode>();
        var visitedLocalFunctions = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        pendingBodies.Push(block);
        while (pendingBodies.Count > 0)
        {
            foreach (var invocation in pendingBodies.Pop()
                         .DescendantNodesAndSelf(descendIntoChildren: static node =>
                             node is not AnonymousFunctionExpressionSyntax
                                 and not LocalFunctionStatementSyntax)
                         .OfType<InvocationExpressionSyntax>())
            {
                if (IsTaskLike(semanticModel.GetTypeInfo(invocation, cancellationToken).Type)
                    && !invocation.Ancestors()
                        .TakeWhile(static ancestor => ancestor is not StatementSyntax)
                        .Any(static ancestor => ancestor is AwaitExpressionSyntax))
                {
                    return true;
                }

                if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol
                        is not IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFunction
                    || !visitedLocalFunctions.Add(localFunction))
                {
                    continue;
                }

                foreach (var syntaxReference in localFunction.DeclaringSyntaxReferences)
                {
                    if (GetLocalFunctionBody(syntaxReference.GetSyntax(cancellationToken))
                        is { } localBody)
                    {
                        pendingBodies.Push(localBody);
                    }
                }
            }
        }

        return false;
    }

    private static SyntaxNode? GetLocalFunctionBody(SyntaxNode declaration) => declaration switch
    {
        LocalFunctionStatementSyntax { Body: { } body } => body,
        LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression } => expression,
        _ => null,
    };

    private static bool IsTaskLike(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.ToDisplayString() is
            "System.Threading.Tasks.Task"
                or "System.Threading.Tasks.Task<TResult>"
                or "System.Threading.Tasks.ValueTask"
                or "System.Threading.Tasks.ValueTask<TResult>";

    private static bool IsAlreadyAssigned(
        AssignmentExpressionSyntax assignment,
        IPropertySymbol property,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        SyntaxNode scope = assignment.FirstAncestorOrSelf<InitializerExpressionSyntax>(
                static initializer => initializer.IsKind(SyntaxKind.ObjectInitializerExpression))
            ?? assignment.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>()
            ?? assignment.FirstAncestorOrSelf<LocalFunctionStatementSyntax>()
            ?? assignment.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>()
            ?? assignment.FirstAncestorOrSelf<BlockSyntax>()
            ?? assignment.Parent!;
        foreach (var candidate in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (ReferenceEquals(candidate, assignment)
                || assignment.Right.Span.Contains(candidate.Span))
            {
                continue;
            }

            var assignedProperty = semanticModel.GetSymbolInfo(candidate.Left, cancellationToken).Symbol;
            if (SymbolEqualityComparer.Default.Equals(assignedProperty, property)
                && HasSameReceiver(assignment.Left, candidate.Left, semanticModel, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSameReceiver(
        ExpressionSyntax first,
        ExpressionSyntax second,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var firstReceiver = GetReceiver(first);
        var secondReceiver = GetReceiver(second);
        if (firstReceiver is null || secondReceiver is null)
        {
            return firstReceiver is null && secondReceiver is null;
        }

        var firstSymbol = semanticModel.GetSymbolInfo(firstReceiver, cancellationToken).Symbol;
        var secondSymbol = semanticModel.GetSymbolInfo(secondReceiver, cancellationToken).Symbol;
        if (!TryResolveStableAlias(
                firstSymbol,
                semanticModel,
                cancellationToken,
                new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                out firstSymbol)
            || !TryResolveStableAlias(
                secondSymbol,
                semanticModel,
                cancellationToken,
                new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                out secondSymbol))
        {
            return true;
        }

        return SymbolEqualityComparer.Default.Equals(firstSymbol, secondSymbol);
    }

    private static bool TryResolveStableAlias(
        ISymbol? symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        HashSet<ISymbol> visited,
        out ISymbol resolved)
    {
        if (symbol is not ILocalSymbol local)
        {
            resolved = symbol!;
            return symbol is not null;
        }

        var declarations = local.DeclaringSyntaxReferences;
        if (!visited.Add(local)
            || declarations.Length != 1
            || declarations[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax
            {
                Initializer.Value: { } initializer,
            } declarator
            || semanticModel.SyntaxTree != declarator.SyntaxTree
            || IsWrittenAfterDeclaration(local, declarator, semanticModel, cancellationToken))
        {
            resolved = null!;
            return false;
        }

        var initializerSymbol = semanticModel.GetSymbolInfo(
            UnwrapReceiver(initializer),
            cancellationToken).Symbol;
        return TryResolveStableAlias(
            initializerSymbol,
            semanticModel,
            cancellationToken,
            visited,
            out resolved);
    }

    private static bool IsWrittenAfterDeclaration(
        ILocalSymbol local,
        VariableDeclaratorSyntax declarator,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        SyntaxNode scope = declarator.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>()
            ?? declarator.FirstAncestorOrSelf<BlockSyntax>()
            ?? declarator.Parent!;
        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText == local.Name
                && SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    local)
                && IsWritten(identifier))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWritten(IdentifierNameSyntax identifier)
    {
        foreach (var ancestor in identifier.Ancestors())
        {
            switch (ancestor)
            {
                case MemberAccessExpressionSyntax memberAccess
                    when memberAccess.Expression.Span.Contains(identifier.Span):
                case ElementAccessExpressionSyntax elementAccess
                    when elementAccess.Expression.Span.Contains(identifier.Span):
                    return false;
                case AssignmentExpressionSyntax assignment
                    when assignment.Left.Span.Contains(identifier.Span):
                    return true;
                case PrefixUnaryExpressionSyntax prefix
                    when prefix.IsKind(SyntaxKind.PreIncrementExpression)
                        || prefix.IsKind(SyntaxKind.PreDecrementExpression):
                    return true;
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.PostIncrementExpression)
                        || postfix.IsKind(SyntaxKind.PostDecrementExpression):
                    return true;
                case ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None):
                    return true;
                case StatementSyntax:
                    return false;
            }
        }

        return false;
    }

    private static ExpressionSyntax UnwrapReceiver(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static ExpressionSyntax? GetReceiver(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null;

    private static bool HasCompatibleResult(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.ValueTask";

    private static async Task<Document> RenamePropertyAsync(
        Document document,
        AssignmentExpressionSyntax assignment,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var replacement = assignment.Left switch
        {
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.WithName(SyntaxFactory.IdentifierName(propertyName)),
            IdentifierNameSyntax identifier =>
                identifier.WithIdentifier(SyntaxFactory.Identifier(propertyName)),
            _ => assignment.Left,
        };
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return document.WithSyntaxRoot(root!.ReplaceNode(
            assignment.Left,
            replacement.WithTriviaFrom(assignment.Left)));
    }
}
