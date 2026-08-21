using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Kevlar.Analyzers;

/// <summary>
/// KEV001: the delegate passed to a Kevlar execute method never uses its effective
/// <see cref="System.Threading.CancellationToken"/>, either from a direct parameter or from
/// <see cref="Kevlar.KevlarContext.CancellationToken"/>. Timeout strategies and caller cancellation
/// work by cancelling that token, so ignoring it means the work cannot be stopped.
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
            if (argument.Value is not IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda })
            {
                continue;
            }

            if (argument.Parameter?.Name == "initializeProperties")
            {
                continue;
            }

            var contextParameter = FindExecutionContextParameter(
                lambda.Symbol,
                argument.Parameter,
                context.Compilation);
            if (contextParameter is not null)
            {
                if (contextParameter.Name != "_"
                    && !UsesContextCancellationToken(lambda, contextParameter, context.Compilation))
                {
                    Report(context, lambda, method);
                }

                continue;
            }

            var tokenParameter = FindExecutionCancellationTokenParameter(lambda.Symbol);
            if (tokenParameter is not null
                && tokenParameter.Name != "_"
                && !UsesParameter(lambda, tokenParameter))
            {
                Report(context, lambda, method);
            }
        }
    }

    private static bool IsKevlarExecutionMethod(IMethodSymbol method, Compilation compilation)
    {
        if (method.Name is not ("Execute" or "ExecuteAsync" or "ExecuteOutcomeAsync"
            or "ExecuteWithContext" or "ExecuteWithContextAsync"))
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

    private static IParameterSymbol? FindExecutionContextParameter(
        IMethodSymbol lambda,
        IParameterSymbol? callbackParameter,
        Compilation compilation)
    {
        var contextType = compilation.GetTypeByMetadataName("Kevlar.KevlarContext");
        if (contextType is null
            || callbackParameter?.OriginalDefinition.Type is not INamedTypeSymbol callbackType
            || callbackType.DelegateInvokeMethod is not { } invokeMethod)
        {
            return null;
        }

        for (var index = invokeMethod.Parameters.Length - 1; index >= 0; index--)
        {
            if (SymbolEqualityComparer.Default.Equals(invokeMethod.Parameters[index].Type, contextType)
                && index < lambda.Parameters.Length)
            {
                return lambda.Parameters[index];
            }
        }

        return null;
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

    private static bool UsesContextCancellationToken(
        IOperation root,
        IParameterSymbol contextParameter,
        Compilation compilation)
    {
        var contextType = compilation.GetTypeByMetadataName("Kevlar.KevlarContext");
        var aliases = FindLocalAliases(root, contextParameter);
        foreach (var operation in Descendants(root))
        {
            if (operation is IPropertyReferenceOperation
                {
                    Property.Name: "CancellationToken",
                    Property.ContainingType: { } containingType,
                    Instance: { } instance,
                }
                && SymbolEqualityComparer.Default.Equals(containingType, contextType)
                && ReferencesAlias(instance, aliases))
            {
                return true;
            }

            if (IsAliasReference(operation, aliases) && IsDirectArgumentValue(operation))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<ISymbol> FindLocalAliases(IOperation root, IParameterSymbol contextParameter)
    {
        var aliases = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { contextParameter };
        var declarators = Descendants(root).OfType<IVariableDeclaratorOperation>().ToArray();

        bool foundAlias;
        do
        {
            foundAlias = false;
            foreach (var declarator in declarators)
            {
                if (declarator.Initializer is { Value: { } value }
                    && ReferencesAlias(value, aliases)
                    && aliases.Add(declarator.Symbol))
                {
                    foundAlias = true;
                }
            }
        }
        while (foundAlias);

        return aliases;
    }

    private static bool ReferencesAlias(IOperation operation, HashSet<ISymbol> aliases)
    {
        operation = Unwrap(operation);
        return IsAliasReference(operation, aliases);
    }

    private static bool IsAliasReference(IOperation operation, HashSet<ISymbol> aliases) =>
        operation switch
        {
            IParameterReferenceOperation parameter => aliases.Contains(parameter.Parameter),
            ILocalReferenceOperation local => aliases.Contains(local.Local),
            _ => false,
        };

    private static bool IsDirectArgumentValue(IOperation operation)
    {
        while (operation.Parent is IConversionOperation or IParenthesizedOperation)
        {
            operation = operation.Parent;
        }

        return operation.Parent is IArgumentOperation argument && argument.Value == operation;
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static void Report(
        OperationAnalysisContext context,
        IAnonymousFunctionOperation lambda,
        IMethodSymbol method) =>
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            lambda.Syntax.GetLocation(),
            method.Name));

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
