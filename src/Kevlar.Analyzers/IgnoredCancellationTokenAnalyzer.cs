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

        if (!IsKevlarExecutionMethod(method, context.Compilation))
        {
            return;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (!AcceptsCancellationTokenDelegate(argument.Parameter)
                || argument.Value is not IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda })
            {
                continue;
            }

            var tokenParameter = FindExecutionCancellationTokenParameter(lambda.Symbol);
            if (tokenParameter is null || tokenParameter.Name == "_" || UsesParameter(lambda, tokenParameter))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                lambda.Syntax.GetLocation(),
                method.Name));
        }
    }

    private static bool IsKevlarExecutionMethod(IMethodSymbol method, Compilation compilation)
    {
        if (method.Name is not ("Execute" or "ExecuteAsync" or "ExecuteOutcomeAsync"))
        {
            return false;
        }

        var containingType = method.ContainingType.OriginalDefinition;
        return IsType(containingType, compilation.GetTypeByMetadataName("Kevlar.Shield"))
            || IsType(containingType, compilation.GetTypeByMetadataName("Kevlar.Shield`1"))
            || IsType(containingType, compilation.GetTypeByMetadataName("Kevlar.ShieldTaskExtensions"));
    }

    private static bool IsType(INamedTypeSymbol type, INamedTypeSymbol? expected) =>
        expected is not null && SymbolEqualityComparer.Default.Equals(type, expected);

    private static bool AcceptsCancellationTokenDelegate(IParameterSymbol? parameter)
    {
        if (parameter?.Type is not INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { } invokeMethod })
        {
            return false;
        }

        return FindExecutionCancellationTokenParameter(invokeMethod) is not null;
    }

    private static IParameterSymbol? FindExecutionCancellationTokenParameter(IMethodSymbol method)
    {
        for (var index = method.Parameters.Length - 1; index >= 0; index--)
        {
            var parameter = method.Parameters[index];
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
