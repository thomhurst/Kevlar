using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Kevlar.Analyzers;

/// <summary>
/// Diagnoses statically provable Kevlar pipeline hazards: synchronous multi-attempt hedging, reactive
/// strategies made unreachable by an inner fallback, per-execution construction of stateful shields,
/// void fallbacks used for result-returning executions, and hedging on an untyped shield.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PipelineHazardAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The KEV002 rule.</summary>
    public static readonly DiagnosticDescriptor SynchronousHedgingRule = new(
        id: "KEV002",
        title: "Multi-attempt hedging requires asynchronous execution",
        messageFormat: "This shield contains multi-attempt hedging, which cannot run through synchronous 'Execute'. Use 'ExecuteAsync' or remove hedging.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Multi-attempt hedging races concurrent attempts and is only supported by Kevlar's asynchronous execution boundary. Synchronous Execute fails for a pipeline containing statically known multi-attempt hedging.");

    /// <summary>The KEV003 rule.</summary>
    public static readonly DiagnosticDescriptor UnreachableReactiveStrategyRule = new(
        id: "KEV003",
        title: "Fallback makes a reactive strategy unreachable",
        messageFormat: "Fallback is inside '{0}' with the same handling clause, so '{0}' cannot observe a handled failure. Chain Fallback first or give it a narrower handling clause.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A fallback inside retry, hedging, or circuit breaker under the same handling clause recovers every handled failure before the outer strategy can observe it. Kevlar rejects this pipeline at construction time.");

    /// <summary>The KEV004 rule.</summary>
    public static readonly DiagnosticDescriptor EphemeralStatefulShieldRule = new(
        id: "KEV004",
        title: "Stateful shield is constructed per execution",
        messageFormat: "'{0}' creates resilience state for one execution. Store and reuse the shield or partition provider as a field, singleton/keyed DI registration, or registry entry.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Circuit breakers, rate limiters, concurrency limiters, and partition providers must outlive individual executions so their resilience state is retained and shared.");

    /// <summary>The KEV005 rule.</summary>
    public static readonly DiagnosticDescriptor VoidFallbackResultExecutionRule = new(
        id: "KEV005",
        title: "Fallback on a non-generic Shield applies only to void executions",
        messageFormat: "Fallback on a non-generic Shield applies only to void executions. For executions that return a value, build a result-aware shield with Shield.For<T>() and use its Fallback overloads.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A void fallback cannot produce a value for a result-returning execution and fails at runtime when it handles an outcome.");

    /// <summary>The KEV006 rule.</summary>
    public static readonly DiagnosticDescriptor UntypedHedgingRule = new(
        id: "KEV006",
        title: "Hedging on an untyped Shield requires an idempotent action",
        messageFormat: "Hedging on an untyped Shield runs the execution delegate more than once, concurrently. Build a result-aware shield with Shield.For<T>() so result clauses can select the winning attempt, or confirm the action is idempotent.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An untyped Shield can only judge hedged attempts by their exceptions, so every attempt it launches runs to completion against the real dependency. Duplicate writes, charges, or sends from a losing hedge are observable unless the action is idempotent.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            SynchronousHedgingRule,
            UnreachableReactiveStrategyRule,
            EphemeralStatefulShieldRule,
            VoidFallbackResultExecutionRule,
            UntypedHedgingRule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var knownTypes = new KnownTypes(compilationContext.Compilation);
            compilationContext.RegisterOperationAction(
                context => AnalyzeInvocation(context, knownTypes),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, KnownTypes knownTypes)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (IsSynchronousExecute(invocation.TargetMethod, knownTypes)
            && FindInPipeline(
                GetReceiver(invocation),
                context,
                candidate => IsKnownMultiAttemptHedge(candidate, knownTypes),
                knownTypes,
                stopAtHandlingClause: false,
                stopAtCompositionBoundary: false,
                out _))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                SynchronousHedgingRule,
                invocation.Syntax.GetLocation()));
        }

        if (IsKevlarFluentMethod(invocation.TargetMethod, knownTypes, "Fallback")
            && HasLocalHandlingOverride(invocation, context) is false
            && FindInPipeline(
                GetReceiver(invocation),
                context,
                candidate => IsReactiveStrategyWithAmbientHandling(candidate, context, knownTypes),
                knownTypes,
                stopAtHandlingClause: true,
                stopAtCompositionBoundary: true,
                out var reactiveStrategy))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnreachableReactiveStrategyRule,
                invocation.Syntax.GetLocation(),
                reactiveStrategy));
        }

        if (IsCompositionBoundary(Normalize(invocation.TargetMethod), knownTypes)
            && FindCompositionHazard(invocation, context, knownTypes, out reactiveStrategy))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnreachableReactiveStrategyRule,
                invocation.Syntax.GetLocation(),
                reactiveStrategy));
        }

        if (!knownTypes.IsTestAssembly
            && !IsTestContext(context.ContainingSymbol)
            && IsExecution(invocation.TargetMethod, knownTypes)
            && TryFindEphemeralStatefulConstruction(
                GetReceiver(invocation),
                context,
                knownTypes,
                visitedLocals: null,
                out var statefulComponent,
                out var location))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EphemeralStatefulShieldRule,
                location,
                statefulComponent));
        }

        if (IsUntypedHedge(invocation.TargetMethod, knownTypes))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UntypedHedgingRule,
                invocation.Syntax.GetLocation()));
        }

        if (IsResultReturningExecution(invocation.TargetMethod, knownTypes)
            && FindInPipeline(
                GetReceiver(invocation),
                context,
                candidate => IsVoidFallback(candidate.TargetMethod, knownTypes),
                knownTypes,
                stopAtHandlingClause: false,
                stopAtCompositionBoundary: false,
                out _))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                VoidFallbackResultExecutionRule,
                invocation.Syntax.GetLocation()));
        }
    }

    private static bool TryFindEphemeralStatefulConstruction(
        IOperation? operation,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        HashSet<ISymbol>? visitedLocals,
        out string? statefulComponent,
        out Location? location)
    {
        operation = Unwrap(operation);

        if (operation is ILocalReferenceOperation localReference)
        {
            visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (!visitedLocals.Add(localReference.Local)
                || !TryGetSingleUseInitializer(localReference, context, out var initializer))
            {
                statefulComponent = null;
                location = null;
                return false;
            }

            var found = TryFindEphemeralStatefulConstruction(
                initializer,
                context,
                knownTypes,
                visitedLocals,
                out statefulComponent,
                out location);
            visitedLocals.Remove(localReference.Local);
            return found;
        }

        if (operation is IConditionalAccessOperation conditionalAccess)
        {
            return TryFindEphemeralStatefulConstruction(
                conditionalAccess.Operation,
                context,
                knownTypes,
                visitedLocals,
                out statefulComponent,
                out location);
        }

        if (operation is IArrayCreationOperation { Initializer: { } arrayInitializer })
        {
            foreach (var element in arrayInitializer.ElementValues)
            {
                if (TryFindEphemeralStatefulConstruction(
                    element,
                    context,
                    knownTypes,
                    visitedLocals,
                    out statefulComponent,
                    out location))
                {
                    return true;
                }
            }

            statefulComponent = null;
            location = null;
            return false;
        }

        if (operation?.Syntax is CollectionExpressionSyntax or SpreadElementSyntax)
        {
            foreach (var child in operation.ChildOperations)
            {
                if (TryFindEphemeralStatefulConstruction(
                    child,
                    context,
                    knownTypes,
                    visitedLocals,
                    out statefulComponent,
                    out location))
                {
                    return true;
                }
            }

            statefulComponent = null;
            location = null;
            return false;
        }

        if (operation is not IInvocationOperation invocation)
        {
            statefulComponent = null;
            location = null;
            return false;
        }

        var method = Normalize(invocation.TargetMethod);
        if (IsStatefulStrategy(method, knownTypes))
        {
            statefulComponent = method.Name;
            location = invocation.Syntax.GetLocation();
            return true;
        }

        if (method.Name == "GetShield" && knownTypes.IsPartitionedShield(method.ContainingType))
        {
            return TryFindEphemeralPartitionProvider(
                GetReceiver(invocation),
                context,
                knownTypes,
                visitedLocals,
                out statefulComponent,
                out location);
        }

        if (!IsKevlarFluentMethod(method, knownTypes))
        {
            statefulComponent = null;
            location = null;
            return false;
        }

        if (IsCompositionBoundary(method, knownTypes))
        {
            foreach (var argument in invocation.Arguments)
            {
                if (TryFindEphemeralStatefulConstruction(
                    argument.Value,
                    context,
                    knownTypes,
                    visitedLocals,
                    out statefulComponent,
                    out location))
                {
                    return true;
                }
            }
        }

        return TryFindEphemeralStatefulConstruction(
            GetReceiver(invocation),
            context,
            knownTypes,
            visitedLocals,
            out statefulComponent,
            out location);
    }

    private static bool TryFindEphemeralPartitionProvider(
        IOperation? operation,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        HashSet<ISymbol>? visitedLocals,
        out string? statefulComponent,
        out Location? location)
    {
        operation = Unwrap(operation);
        if (operation is ILocalReferenceOperation localReference)
        {
            visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (!visitedLocals.Add(localReference.Local)
                || !TryGetSingleUseInitializer(localReference, context, out var initializer))
            {
                statefulComponent = null;
                location = null;
                return false;
            }

            var found = TryFindEphemeralPartitionProvider(
                initializer,
                context,
                knownTypes,
                visitedLocals,
                out statefulComponent,
                out location);
            visitedLocals.Remove(localReference.Local);
            return found;
        }

        if (operation is IObjectCreationOperation creation
            && creation.Type is INamedTypeSymbol type
            && knownTypes.IsPartitionedShield(type))
        {
            statefulComponent = "PartitionedShield";
            location = creation.Syntax.GetLocation();
            return true;
        }

        statefulComponent = null;
        location = null;
        return false;
    }

    private static bool FindInPipeline(
        IOperation? operation,
        OperationAnalysisContext context,
        Func<IInvocationOperation, bool> predicate,
        KnownTypes knownTypes,
        bool stopAtHandlingClause,
        bool stopAtCompositionBoundary,
        out string? matchedMethod)
        => FindInPipeline(
            operation,
            context,
            predicate,
            knownTypes,
            stopAtHandlingClause,
            stopAtCompositionBoundary,
            visitedLocals: null,
            out matchedMethod);

    private static bool FindInPipeline(
        IOperation? operation,
        OperationAnalysisContext context,
        Func<IInvocationOperation, bool> predicate,
        KnownTypes knownTypes,
        bool stopAtHandlingClause,
        bool stopAtCompositionBoundary,
        HashSet<ISymbol>? visitedLocals,
        out string? matchedMethod)
    {
        operation = Unwrap(operation);

        if (operation is ILocalReferenceOperation localReference)
        {
            visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (!visitedLocals.Add(localReference.Local)
                || !TryGetStableInitializer(localReference, context, out var initializer))
            {
                matchedMethod = null;
                return false;
            }

            var found = FindInPipeline(
                initializer,
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod);
            visitedLocals.Remove(localReference.Local);
            return found;
        }

        if (operation is IConditionalAccessOperation conditionalAccess)
        {
            return FindInPipeline(
                conditionalAccess.Operation,
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod);
        }

        if (operation is IArrayCreationOperation { Initializer: { } arrayInitializer })
        {
            foreach (var element in arrayInitializer.ElementValues)
            {
                if (FindInPipeline(
                    element,
                    context,
                    predicate,
                    knownTypes,
                    stopAtHandlingClause,
                    stopAtCompositionBoundary,
                    visitedLocals,
                    out matchedMethod))
                {
                    return true;
                }
            }

            matchedMethod = null;
            return false;
        }

        if (operation?.Syntax is CollectionExpressionSyntax or SpreadElementSyntax)
        {
            foreach (var child in operation.ChildOperations)
            {
                if (FindInPipeline(
                    child,
                    context,
                    predicate,
                    knownTypes,
                    stopAtHandlingClause,
                    stopAtCompositionBoundary,
                    visitedLocals,
                    out matchedMethod))
                {
                    return true;
                }
            }

            matchedMethod = null;
            return false;
        }

        if (operation is not IInvocationOperation invocation)
        {
            matchedMethod = null;
            return false;
        }

        var method = Normalize(invocation.TargetMethod);
        if (stopAtHandlingClause
            && method.Name == "WhenAnyError")
        {
            return FindInDefaultHandlingSegments(
                GetReceiver(invocation),
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod);
        }

        if (stopAtHandlingClause
            && StartsHandlingClause(method, knownTypes))
        {
            matchedMethod = null;
            return false;
        }

        if (predicate(invocation))
        {
            matchedMethod = method.Name;
            return true;
        }

        var isCompositionBoundary = IsCompositionBoundary(method, knownTypes);
        if (stopAtCompositionBoundary && isCompositionBoundary)
        {
            return FindAtCompositionBoundary(
                invocation,
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod);
        }

        if (!IsKevlarFluentMethod(method, knownTypes))
        {
            matchedMethod = null;
            return false;
        }

        if (isCompositionBoundary)
        {
            foreach (var argument in invocation.Arguments)
            {
                if (FindInPipeline(
                    argument.Value,
                    context,
                    predicate,
                    knownTypes,
                    stopAtHandlingClause,
                    stopAtCompositionBoundary,
                    visitedLocals,
                    out matchedMethod))
                {
                    return true;
                }
            }
        }

        return FindInPipeline(
            GetReceiver(invocation),
            context,
            predicate,
            knownTypes,
            stopAtHandlingClause,
            stopAtCompositionBoundary,
            visitedLocals,
            out matchedMethod);
    }

    private static bool FindCompositionHazard(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        out string? matchedMethod)
    {
        var operands = new List<IOperation>();
        var method = Normalize(invocation.TargetMethod);
        if (method.Name == "Wrap")
        {
            var outer = GetReceiver(invocation);
            var inner = GetArgument(invocation, "inner");
            if (outer is null || inner is null)
            {
                matchedMethod = null;
                return false;
            }

            operands.Add(outer);
            operands.Add(inner);
        }
        else
        {
            foreach (var argument in invocation.Arguments)
            {
                CollectCompositionOperands(argument.Value, context, operands, visitedLocals: null);
            }
        }

        var ambientClauses = new SyntaxNode?[operands.Count];
        for (var index = 0; index < operands.Count; index++)
        {
            if (!TryGetAmbientClause(
                operands[index],
                context,
                knownTypes,
                visitedLocals: null,
                out ambientClauses[index]))
            {
                matchedMethod = null;
                return false;
            }
        }

        for (var fallbackIndex = 1; fallbackIndex < operands.Count; fallbackIndex++)
        {
            if (!FindInPipeline(
                operands[fallbackIndex],
                context,
                candidate => IsKevlarFluentMethod(candidate.TargetMethod, knownTypes, "Fallback")
                    && HasLocalHandlingOverride(candidate, context) is false,
                knownTypes,
                stopAtHandlingClause: true,
                stopAtCompositionBoundary: true,
                out _))
            {
                continue;
            }

            for (var reactiveIndex = 0; reactiveIndex < fallbackIndex; reactiveIndex++)
            {
                if (SameAmbient(ambientClauses[reactiveIndex], ambientClauses[fallbackIndex])
                    && FindInPipeline(
                        operands[reactiveIndex],
                        context,
                        candidate => IsReactiveStrategyWithAmbientHandling(candidate, context, knownTypes),
                        knownTypes,
                        stopAtHandlingClause: true,
                        stopAtCompositionBoundary: true,
                        out matchedMethod))
                {
                    return true;
                }
            }
        }

        matchedMethod = null;
        return false;
    }

    private static bool FindAtCompositionBoundary(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        Func<IInvocationOperation, bool> predicate,
        KnownTypes knownTypes,
        bool stopAtHandlingClause,
        bool stopAtCompositionBoundary,
        HashSet<ISymbol>? visitedLocals,
        out string? matchedMethod)
    {
        var method = Normalize(invocation.TargetMethod);
        if (method.Name == "Compose")
        {
            return FindInComposeDefaultAmbient(
                invocation,
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod);
        }

        var outer = GetReceiver(invocation);
        var inner = GetArgument(invocation, "inner");
        if (inner is null)
        {
            matchedMethod = null;
            return false;
        }

        if (FindInDefaultHandlingSegments(
                inner,
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod))
        {
            return true;
        }

        return FindInDefaultHandlingSegments(
            outer,
            context,
            predicate,
            knownTypes,
            stopAtHandlingClause,
            stopAtCompositionBoundary,
            visitedLocals,
            out matchedMethod);
    }

    private static bool FindInComposeDefaultAmbient(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        Func<IInvocationOperation, bool> predicate,
        KnownTypes knownTypes,
        bool stopAtHandlingClause,
        bool stopAtCompositionBoundary,
        HashSet<ISymbol>? visitedLocals,
        out string? matchedMethod)
    {
        var operands = new List<IOperation>();
        foreach (var argument in invocation.Arguments)
        {
            CollectCompositionOperands(argument.Value, context, operands, visitedLocals);
        }

        foreach (var operand in operands)
        {
            if (FindInDefaultHandlingSegments(
                operand,
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod))
            {
                return true;
            }
        }

        matchedMethod = null;
        return false;
    }

    private static bool FindInDefaultHandlingSegments(
        IOperation? operation,
        OperationAnalysisContext context,
        Func<IInvocationOperation, bool> predicate,
        KnownTypes knownTypes,
        bool stopAtHandlingClause,
        bool stopAtCompositionBoundary,
        HashSet<ISymbol>? visitedLocals,
        out string? matchedMethod)
    {
        operation = Unwrap(operation);
        if (operation is ILocalReferenceOperation localReference)
        {
            visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (!visitedLocals.Add(localReference.Local)
                || !TryGetStableInitializer(localReference, context, out var initializer))
            {
                matchedMethod = null;
                return false;
            }

            var found = FindInDefaultHandlingSegments(
                initializer,
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod);
            visitedLocals.Remove(localReference.Local);
            return found;
        }

        if (operation is IConditionalAccessOperation conditionalAccess)
        {
            return FindInDefaultHandlingSegments(
                conditionalAccess.Operation,
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod);
        }

        if (!TryGetAmbientClause(
                operation,
                context,
                knownTypes,
                visitedLocals,
                out var ambientClause))
        {
            matchedMethod = null;
            return false;
        }

        if (ambientClause is null
            && FindInPipeline(
                operation,
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod))
        {
            return true;
        }

        if (operation is not IInvocationOperation invocation
            || !IsKevlarFluentMethod(invocation.TargetMethod, knownTypes)
            || IsCompositionBoundary(Normalize(invocation.TargetMethod), knownTypes))
        {
            matchedMethod = null;
            return false;
        }

        return FindInDefaultHandlingSegments(
            GetReceiver(invocation),
            context,
            predicate,
            knownTypes,
            stopAtHandlingClause,
            stopAtCompositionBoundary,
            visitedLocals,
            out matchedMethod);
    }

    private static void CollectCompositionOperands(
        IOperation? operation,
        OperationAnalysisContext context,
        List<IOperation> operands,
        HashSet<ISymbol>? visitedLocals)
    {
        operation = Unwrap(operation);
        if (operation is ILocalReferenceOperation localReference)
        {
            visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (visitedLocals.Add(localReference.Local))
            {
                if (TryGetStableInitializer(localReference, context, out var initializer))
                {
                    CollectCompositionOperands(initializer, context, operands, visitedLocals);
                    visitedLocals.Remove(localReference.Local);
                    return;
                }

                visitedLocals.Remove(localReference.Local);
            }
        }

        if (operation is IArrayCreationOperation { Initializer: { } arrayInitializer })
        {
            foreach (var element in arrayInitializer.ElementValues)
            {
                CollectCompositionOperands(element, context, operands, visitedLocals);
            }

            return;
        }

        if (operation?.Syntax is CollectionExpressionSyntax or SpreadElementSyntax)
        {
            foreach (var child in operation.ChildOperations)
            {
                CollectCompositionOperands(child, context, operands, visitedLocals);
            }

            return;
        }

        if (operation is not null)
        {
            operands.Add(operation);
        }
    }

    private static IOperation? GetArgument(IInvocationOperation invocation, string parameterName)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == parameterName)
            {
                return argument.Value;
            }
        }

        return null;
    }

    private static bool TryGetAmbientClause(
        IOperation? operation,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        HashSet<ISymbol>? visitedLocals,
        out SyntaxNode? ambientClause)
    {
        operation = Unwrap(operation);
        if (operation is ILocalReferenceOperation localReference)
        {
            visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (!visitedLocals.Add(localReference.Local)
                || !TryGetStableInitializer(localReference, context, out var initializer))
            {
                ambientClause = null;
                return false;
            }

            var found = TryGetAmbientClause(
                initializer,
                context,
                knownTypes,
                visitedLocals,
                out ambientClause);
            visitedLocals.Remove(localReference.Local);
            return found;
        }

        if (operation is IConditionalAccessOperation conditionalAccess)
        {
            return TryGetAmbientClause(
                conditionalAccess.Operation,
                context,
                knownTypes,
                visitedLocals,
                out ambientClause);
        }

        if (operation is IPropertyReferenceOperation propertyReference)
        {
            ambientClause = null;
            return propertyReference.Property.Name == "Empty"
                && knownTypes.IsShield(propertyReference.Property.ContainingType);
        }

        if (operation is not IInvocationOperation invocation)
        {
            ambientClause = null;
            return false;
        }

        var method = Normalize(invocation.TargetMethod);
        if (method.Name == "WhenAnyError" && StartsHandlingClause(method, knownTypes))
        {
            ambientClause = null;
            return true;
        }

        if (StartsHandlingClause(method, knownTypes))
        {
            ambientClause = invocation.Syntax;
            return true;
        }

        if (!IsKevlarFluentMethod(method, knownTypes))
        {
            ambientClause = null;
            return false;
        }

        if (knownTypes.IsShieldBuilder(method.ContainingType)
            && method.ReturnType is INamedTypeSymbol returnType
            && knownTypes.IsShield(returnType)
            && BuilderCreatesHandlingClause(
                GetReceiver(invocation),
                context,
                knownTypes,
                visitedLocals))
        {
            ambientClause = invocation.Syntax;
            return true;
        }

        if (method.Name == "Wrap")
        {
            ambientClause = null;
            return true;
        }

        if (method.Name == "Compose")
        {
            ambientClause = null;
            return true;
        }

        var receiver = GetReceiver(invocation);
        if (receiver is not null)
        {
            return TryGetAmbientClause(
                receiver,
                context,
                knownTypes,
                visitedLocals,
                out ambientClause);
        }

        ambientClause = null;
        return knownTypes.IsShield(method.ContainingType);
    }

    private static bool BuilderCreatesHandlingClause(
        IOperation? operation,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        HashSet<ISymbol>? visitedLocals)
    {
        operation = Unwrap(operation);
        if (operation is ILocalReferenceOperation localReference)
        {
            visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (!visitedLocals.Add(localReference.Local)
                || !TryGetStableInitializer(localReference, context, out var initializer))
            {
                return false;
            }

            var createsClause = BuilderCreatesHandlingClause(
                initializer,
                context,
                knownTypes,
                visitedLocals);
            visitedLocals.Remove(localReference.Local);
            return createsClause;
        }

        if (operation is not IInvocationOperation invocation)
        {
            return false;
        }

        var method = Normalize(invocation.TargetMethod);
        return StartsHandlingClause(method, knownTypes)
            || knownTypes.IsShieldBuilder(method.ContainingType)
                && BuilderCreatesHandlingClause(
                    GetReceiver(invocation),
                    context,
                    knownTypes,
                    visitedLocals);
    }

    private static bool SameAmbient(SyntaxNode? left, SyntaxNode? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SyntaxTree == right.SyntaxTree && left.Span == right.Span;
    }

    private static bool WrapPreservesAmbient(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        HashSet<ISymbol>? visitedLocals)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == "inner")
            {
                return IsKnownClauseFree(argument.Value, context, knownTypes, visitedLocals);
            }
        }

        return false;
    }

    private static bool IsKnownClauseFree(
        IOperation? operation,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        HashSet<ISymbol>? visitedLocals)
    {
        operation = Unwrap(operation);

        if (operation is ILocalReferenceOperation localReference)
        {
            visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (!visitedLocals.Add(localReference.Local)
                || !TryGetStableInitializer(localReference, context, out var initializer))
            {
                return false;
            }

            var isClauseFree = IsKnownClauseFree(initializer, context, knownTypes, visitedLocals);
            visitedLocals.Remove(localReference.Local);
            return isClauseFree;
        }

        if (operation is IPropertyReferenceOperation propertyReference)
        {
            return propertyReference.Property.Name == "Empty"
                && knownTypes.IsShield(propertyReference.Property.ContainingType);
        }

        if (operation is not IInvocationOperation invocation)
        {
            return false;
        }

        var method = Normalize(invocation.TargetMethod);
        if (StartsHandlingClause(method, knownTypes) || !IsKevlarFluentMethod(method, knownTypes))
        {
            return false;
        }

        if (method.Name == "Wrap")
        {
            return IsKnownClauseFree(GetReceiver(invocation), context, knownTypes, visitedLocals)
                && WrapPreservesAmbient(invocation, context, knownTypes, visitedLocals);
        }

        if (method.Name == "Compose")
        {
            return false;
        }

        var receiver = GetReceiver(invocation);
        return receiver is null
            ? knownTypes.IsShield(method.ContainingType)
            : IsKnownClauseFree(receiver, context, knownTypes, visitedLocals);
    }

    private static bool TryGetStableInitializer(
        ILocalReferenceOperation localReference,
        OperationAnalysisContext context,
        out IOperation? initializer)
        => TryGetInitializer(localReference, context, requireSingleUse: false, out initializer);

    private static bool TryGetSingleUseInitializer(
        ILocalReferenceOperation localReference,
        OperationAnalysisContext context,
        out IOperation? initializer)
        => TryGetInitializer(localReference, context, requireSingleUse: true, out initializer);

    private static bool TryGetInitializer(
        ILocalReferenceOperation localReference,
        OperationAnalysisContext context,
        bool requireSingleUse,
        out IOperation? initializer)
    {
        var local = localReference.Local;
        var declarations = local.DeclaringSyntaxReferences;
        if (declarations.Length != 1
            || declarations[0].GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax
            {
                Initializer.Value: { } initializerSyntax,
            } declarator)
        {
            initializer = null;
            return false;
        }

        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel is null || semanticModel.SyntaxTree != declarator.SyntaxTree)
        {
            initializer = null;
            return false;
        }

        var declarationScope = GetExecutableScope(declarator, context.CancellationToken);
        if (requireSingleUse
            && !SameSyntax(
                declarationScope,
                GetExecutableScope(localReference.Syntax, context.CancellationToken)))
        {
            initializer = null;
            return false;
        }

        var referenceCount = 0;
        foreach (var identifier in declarationScope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText == local.Name
                && SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol,
                    local))
            {
                referenceCount++;
                if (IsWritten(identifier)
                    || local.Type is IArrayTypeSymbol
                        && IsEscapingArrayReference(identifier, localReference.Syntax)
                    || requireSingleUse && referenceCount > 1)
                {
                    initializer = null;
                    return false;
                }
            }
        }

        if (requireSingleUse && referenceCount != 1)
        {
            initializer = null;
            return false;
        }

        initializer = semanticModel.GetOperation(initializerSyntax, context.CancellationToken);
        return initializer is not null;
    }

    private static SyntaxNode GetExecutableScope(SyntaxNode syntax, CancellationToken cancellationToken)
    {
        for (SyntaxNode? current = syntax; current is not null; current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax
                or LocalFunctionStatementSyntax
                or BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax
                or ArrowExpressionClauseSyntax
                or PropertyDeclarationSyntax
                or IndexerDeclarationSyntax)
            {
                return current;
            }
        }

        return syntax.SyntaxTree.GetRoot(cancellationToken);
    }

    private static bool SameSyntax(SyntaxNode left, SyntaxNode right) =>
        left.SyntaxTree == right.SyntaxTree && left.Span == right.Span;

    private static bool IsEscapingArrayReference(
        IdentifierNameSyntax identifier,
        SyntaxNode permittedReference)
    {
        if (identifier.SyntaxTree == permittedReference.SyntaxTree
            && identifier.Span == permittedReference.Span)
        {
            return false;
        }

        foreach (var ancestor in identifier.Ancestors())
        {
            switch (ancestor)
            {
                case ArgumentSyntax:
                case EqualsValueClauseSyntax:
                case ReturnStatementSyntax:
                case YieldStatementSyntax:
                    return true;
                case AssignmentExpressionSyntax assignment when assignment.Right.Span.Contains(identifier.Span):
                    return true;
                case StatementSyntax:
                    return false;
            }
        }

        return true;
    }

    private static bool IsWritten(IdentifierNameSyntax identifier)
    {
        foreach (var ancestor in identifier.Ancestors())
        {
            switch (ancestor)
            {
                case AssignmentExpressionSyntax assignment when assignment.Left.Span.Contains(identifier.Span):
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

    private static IOperation? GetReceiver(IInvocationOperation invocation)
    {
        var instance = Unwrap(invocation.Instance);
        if (instance is IConditionalAccessInstanceOperation)
        {
            for (var parent = invocation.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is IConditionalAccessOperation conditionalAccess)
                {
                    return conditionalAccess.Operation;
                }
            }
        }
        else if (instance is not null)
        {
            return instance;
        }

        var method = Normalize(invocation.TargetMethod);
        if (!method.IsExtensionMethod)
        {
            return null;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == 0)
            {
                return argument.Value;
            }
        }

        return null;
    }

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    private static IMethodSymbol Normalize(IMethodSymbol method) =>
        (method.ReducedFrom ?? method).OriginalDefinition;

    private static bool IsExecution(IMethodSymbol method, KnownTypes knownTypes)
    {
        method = Normalize(method);
        return (method.Name is "Execute" or "ExecuteAsync" or "ExecuteOutcomeAsync"
                or "ExecuteWithContext" or "ExecuteWithContextAsync")
            && (knownTypes.IsShield(method.ContainingType)
                || knownTypes.IsShieldTaskExtensions(method.ContainingType));
    }

    private static bool IsResultReturningExecution(IMethodSymbol method, KnownTypes knownTypes)
    {
        method = Normalize(method);
        return IsExecution(method, knownTypes)
            && !method.ReturnsVoid
            && !knownTypes.IsNonGenericValueTask(method.ReturnType);
    }

    private static bool IsVoidFallback(IMethodSymbol method, KnownTypes knownTypes)
    {
        method = Normalize(method);
        return method.Name == "Fallback"
            && method.ReturnType is INamedTypeSymbol returnType
            && knownTypes.IsUntypedShield(returnType)
            && IsKevlarFluentMethod(method, knownTypes);
    }

    private static bool IsUntypedHedge(IMethodSymbol method, KnownTypes knownTypes)
    {
        method = Normalize(method);
        return method.Name == "Hedge"
            && method.ReturnType is INamedTypeSymbol returnType
            && knownTypes.IsUntypedShield(returnType)
            && IsKevlarFluentMethod(method, knownTypes);
    }

    private static bool IsStatefulStrategy(IMethodSymbol method, KnownTypes knownTypes) =>
        method.Name is "CircuitBreaker" or "RateLimit" or "ConcurrencyLimit"
        && IsKevlarFluentMethod(method, knownTypes);

    private static bool IsTestContext(ISymbol? symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            foreach (var attribute in current.GetAttributes())
            {
                var attributeType = attribute.AttributeClass;
                if (attributeType is not null
                    && attributeType.Name is "TestAttribute" or "FactAttribute" or "TheoryAttribute" or "TestMethodAttribute"
                    && attributeType.ContainingNamespace?.ToDisplayString() is "TUnit.Core"
                        or "Xunit"
                        or "NUnit.Framework"
                        or "Microsoft.VisualStudio.TestTools.UnitTesting")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSynchronousExecute(IMethodSymbol method, KnownTypes knownTypes)
    {
        method = Normalize(method);
        return method.Name == "Execute" && knownTypes.IsShield(method.ContainingType);
    }

    private static bool IsReactiveStrategy(IMethodSymbol method, KnownTypes knownTypes) =>
        (method.Name is "Retry" or "RetryForever" or "Hedge" or "CircuitBreaker")
        && IsKevlarFluentMethod(method, knownTypes);

    private static bool IsReactiveStrategyWithAmbientHandling(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        KnownTypes knownTypes) =>
        IsReactiveStrategy(Normalize(invocation.TargetMethod), knownTypes)
        && HasLocalHandlingOverride(invocation, context) is false;

    private static bool? HasLocalHandlingOverride(
        IInvocationOperation invocation,
        OperationAnalysisContext context)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == "configure")
            {
                return AnalyzeLocalHandlingConfigurator(
                    argument.Value,
                    context,
                    visitedSymbols: null);
            }
        }

        return false;
    }

    private static bool? AnalyzeLocalHandlingConfigurator(
        IOperation operation,
        OperationAnalysisContext context,
        HashSet<ISymbol>? visitedSymbols)
    {
        operation = Unwrap(operation)!;
        if (operation is IDelegateCreationOperation delegateCreation)
        {
            return AnalyzeLocalHandlingConfigurator(delegateCreation.Target, context, visitedSymbols);
        }

        if (operation is ILocalReferenceOperation localReference)
        {
            visitedSymbols ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (!visitedSymbols.Add(localReference.Local)
                || !TryGetStableInitializer(localReference, context, out var initializer)
                || initializer is null)
            {
                return null;
            }

            var result = AnalyzeLocalHandlingConfigurator(initializer, context, visitedSymbols);
            visitedSymbols.Remove(localReference.Local);
            return result;
        }

        if (operation is IMethodReferenceOperation methodReference)
        {
            return ContainsLocalHandlingOverride(methodReference.Method, context, visitedSymbols);
        }

        if (operation is IParameterReferenceOperation
            or IPropertyReferenceOperation
            or IFieldReferenceOperation)
        {
            return null;
        }

        return operation is IAnonymousFunctionOperation
            ? ContainsLocalHandlingOverride(
                operation,
                context,
                visitedSymbols,
                includeAnonymousFunctionBody: true)
            : null;
    }

    private static bool? ContainsLocalHandlingOverride(
        IOperation operation,
        OperationAnalysisContext context,
        HashSet<ISymbol>? visitedSymbols,
        bool includeAnonymousFunctionBody = false)
    {
        operation = Unwrap(operation)!;
        if (operation is IDelegateCreationOperation)
        {
            return false;
        }

        if (operation is IAnonymousFunctionOperation anonymousFunction)
        {
            return includeAnonymousFunctionBody
                ? ContainsLocalHandlingOverride(anonymousFunction.Body, context, visitedSymbols)
                : false;
        }

        if (operation is IAssignmentOperation
            {
                Target: IPropertyReferenceOperation propertyReference,
                Value: { } value,
            }
            && propertyReference.Property.Name is "HandlesException" or "HandlesResult"
            && propertyReference.Property.ContainingNamespace.ToDisplayString() == "Kevlar"
            && value.ConstantValue is not { HasValue: true, Value: null })
        {
            return true;
        }

        bool? result = operation is IInvocationOperation invocation
            ? ContainsLocalHandlingOverride(invocation, context, visitedSymbols)
            : false;
        if (result is true)
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            var childResult = ContainsLocalHandlingOverride(child, context, visitedSymbols);
            if (childResult is true)
            {
                return true;
            }

            if (childResult is null)
            {
                result = null;
            }
        }

        return result;
    }

    private static bool? ContainsLocalHandlingOverride(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        HashSet<ISymbol>? visitedSymbols)
    {
        if (!invocation.TargetMethod.DeclaringSyntaxReferences.IsEmpty)
        {
            return ContainsLocalHandlingOverride(invocation.TargetMethod, context, visitedSymbols);
        }

        if (IsHandlingOptionsType(Unwrap(invocation.Instance)?.Type))
        {
            return null;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (IsHandlingOptionsType(Unwrap(argument.Value)?.Type))
            {
                return null;
            }
        }

        return false;
    }

    private static bool IsHandlingOptionsType(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.ContainingNamespace.ToDisplayString() == "Kevlar"
                && (current.GetMembers("HandlesException").Length > 0
                    || current.GetMembers("HandlesResult").Length > 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool? ContainsLocalHandlingOverride(
        IMethodSymbol method,
        OperationAnalysisContext context,
        HashSet<ISymbol>? visitedSymbols)
    {
        visitedSymbols ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (!visitedSymbols.Add(method))
        {
            return null;
        }

        try
        {
            if (method.DeclaringSyntaxReferences.Length == 0)
            {
                return null;
            }

            bool? result = false;
            foreach (var syntaxReference in method.DeclaringSyntaxReferences)
            {
                var syntax = syntaxReference.GetSyntax(context.CancellationToken);
                var semanticModel = context.Operation.SemanticModel;
                var methodOperation = semanticModel?.SyntaxTree == syntax.SyntaxTree
                    ? semanticModel.GetOperation(syntax, context.CancellationToken)
                    : null;
                if (methodOperation is null)
                {
                    result = null;
                    continue;
                }

                var methodResult = ContainsLocalHandlingOverride(
                    methodOperation,
                    context,
                    visitedSymbols);
                if (methodResult is true)
                {
                    return true;
                }

                if (methodResult is null)
                {
                    result = null;
                }
            }

            return result;
        }
        finally
        {
            visitedSymbols.Remove(method);
        }
    }

    private static bool IsKnownMultiAttemptHedge(
        IInvocationOperation invocation,
        KnownTypes knownTypes)
    {
        if (!IsKevlarFluentMethod(invocation.TargetMethod, knownTypes, "Hedge"))
        {
            return false;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == "maxAttempts"
                && argument.Value.ConstantValue is { HasValue: true, Value: int maxAttempts })
            {
                return maxAttempts > 1;
            }
        }

        return false;
    }

    private static bool StartsHandlingClause(IMethodSymbol method, KnownTypes knownTypes) =>
        (method.Name is "When" or "WhenResult" or "WhenResultDefault" or "WhenAnyError")
        && (knownTypes.IsShield(method.ContainingType)
            || knownTypes.IsShieldExtensions(method.ContainingType));

    private static bool IsCompositionBoundary(IMethodSymbol method, KnownTypes knownTypes) =>
        (method.Name is "Wrap" or "Compose") && IsKevlarFluentMethod(method, knownTypes);

    private static bool IsKevlarFluentMethod(
        IMethodSymbol method,
        KnownTypes knownTypes,
        string? expectedName = null)
    {
        method = Normalize(method);
        return (expectedName is null || method.Name == expectedName)
            && (knownTypes.IsShield(method.ContainingType)
                || knownTypes.IsShieldBuilder(method.ContainingType)
                || knownTypes.IsShieldExtensions(method.ContainingType));
    }

    private sealed class KnownTypes
    {
        private readonly INamedTypeSymbol? _shield;
        private readonly INamedTypeSymbol? _shieldOfT;
        private readonly INamedTypeSymbol? _shieldBuilder;
        private readonly INamedTypeSymbol? _shieldBuilderOfT;
        private readonly INamedTypeSymbol? _shieldExtensions;
        private readonly INamedTypeSymbol? _shieldTaskExtensions;
        private readonly INamedTypeSymbol? _partitionedShield;
        private readonly INamedTypeSymbol? _partitionedShieldOfT;
        private readonly INamedTypeSymbol? _valueTask;

        internal KnownTypes(Compilation compilation)
        {
            _shield = compilation.GetTypeByMetadataName("Kevlar.Shield");
            _shieldOfT = compilation.GetTypeByMetadataName("Kevlar.Shield`1");
            _shieldBuilder = compilation.GetTypeByMetadataName("Kevlar.ShieldBuilder");
            _shieldBuilderOfT = compilation.GetTypeByMetadataName("Kevlar.ShieldBuilder`1");
            _shieldExtensions = compilation.GetTypeByMetadataName("Kevlar.ShieldExtensions");
            _shieldTaskExtensions = compilation.GetTypeByMetadataName("Kevlar.ShieldTaskExtensions");
            _partitionedShield = compilation.GetTypeByMetadataName("Kevlar.PartitionedShield`1");
            _partitionedShieldOfT = compilation.GetTypeByMetadataName("Kevlar.PartitionedShield`2");
            _valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
            var assemblyName = compilation.AssemblyName;
            IsTestAssembly = assemblyName is not null
                && (assemblyName.EndsWith(".Test", StringComparison.OrdinalIgnoreCase)
                    || assemblyName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));
        }

        internal bool IsTestAssembly { get; }

        internal bool IsShield(INamedTypeSymbol type) =>
            Is(type, _shield) || Is(type, _shieldOfT);

        internal bool IsUntypedShield(INamedTypeSymbol type) => Is(type, _shield);

        internal bool IsShieldBuilder(INamedTypeSymbol type) =>
            Is(type, _shieldBuilder) || Is(type, _shieldBuilderOfT);

        internal bool IsShieldExtensions(INamedTypeSymbol type) => Is(type, _shieldExtensions);

        internal bool IsShieldTaskExtensions(INamedTypeSymbol type) => Is(type, _shieldTaskExtensions);

        internal bool IsPartitionedShield(INamedTypeSymbol type) =>
            Is(type, _partitionedShield) || Is(type, _partitionedShieldOfT);

        internal bool IsNonGenericValueTask(ITypeSymbol type) =>
            type is INamedTypeSymbol namedType && Is(namedType, _valueTask);

        private static bool Is(INamedTypeSymbol type, INamedTypeSymbol? expected) =>
            expected is not null
            && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, expected);
    }
}
