using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Kevlar.Analyzers;

/// <summary>
/// KEV001: the delegate passed to a Kevlar execute method never uses the
/// <see cref="System.Threading.CancellationToken"/> it is handed. Timeout strategies and caller
/// cancellation work by cancelling that token, so ignoring it means the work cannot be stopped —
/// the single most common way to defeat a timeout.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoredCancellationTokenAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The KEV001 rule.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "KEV001",
        title: "Execution delegate ignores its CancellationToken",
        messageFormat: "The delegate passed to '{0}' never uses the CancellationToken it is handed; timeouts and cancellation cannot stop it. Pass the token to the work inside, or name it '_' only if the work is truly uncancellable.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Kevlar hands the execution delegate a CancellationToken that timeout strategies and callers cancel. A delegate that ignores it keeps running after the pipeline has given up on it.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!method.Name.StartsWith("Execute", StringComparison.Ordinal) || !IsKevlarShieldType(method.ContainingType))
        {
            return;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Value is not IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda })
            {
                continue;
            }

            var tokenParameter = FindCancellationTokenParameter(lambda.Symbol);
            if (tokenParameter is null || UsesParameter(lambda, tokenParameter))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                argument.Value.Syntax.GetLocation(),
                method.Name));
        }
    }

    private static bool IsKevlarShieldType(INamedTypeSymbol? type) =>
        type is { Name: "Shield" or "ShieldTaskExtensions", ContainingNamespace: { Name: "Kevlar", ContainingNamespace.IsGlobalNamespace: true } };

    private static IParameterSymbol? FindCancellationTokenParameter(IMethodSymbol lambda)
    {
        foreach (var parameter in lambda.Parameters)
        {
            if (parameter.Type is { Name: "CancellationToken", ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } })
            {
                return parameter;
            }
        }

        return null;
    }

    private static bool UsesParameter(IOperation root, IParameterSymbol parameter)
    {
        foreach (var operation in Descendants(root))
        {
            if (operation is IParameterReferenceOperation reference
                && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IOperation> Descendants(IOperation root)
    {
        var stack = new Stack<IOperation>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            foreach (var child in current.ChildOperations)
            {
                stack.Push(child);
            }
        }
    }
}
