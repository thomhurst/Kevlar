using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Kevlar.Analyzers;

/// <summary>
/// Diagnoses statically provable Kevlar pipeline hazards: synchronous multi-attempt hedging, reactive
/// strategies made unreachable by an inner fallback, and per-execution construction of stateful shields.
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

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            SynchronousHedgingRule,
            UnreachableReactiveStrategyRule,
            EphemeralStatefulShieldRule);

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
            && FindInPipeline(
                GetReceiver(invocation),
                context,
                candidate => IsReactiveStrategy(Normalize(candidate.TargetMethod), knownTypes),
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
        if (stopAtHandlingClause && StartsHandlingClause(method, knownTypes))
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
                candidate => IsKevlarFluentMethod(candidate.TargetMethod, knownTypes, "Fallback"),
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
                        candidate => IsReactiveStrategy(Normalize(candidate.TargetMethod), knownTypes),
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
            return FindInComposeAmbient(
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

        if (TryGetAmbientClause(inner, context, knownTypes, visitedLocals, out var innerAmbient)
            && TryGetAmbientClause(outer, context, knownTypes, visitedLocals, out var outerAmbient))
        {
            var resultAmbient = innerAmbient ?? outerAmbient;
            if (SameAmbient(innerAmbient, resultAmbient)
                && FindInPipeline(
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

            if (SameAmbient(outerAmbient, resultAmbient))
            {
                return FindInPipeline(
                    outer,
                    context,
                    predicate,
                    knownTypes,
                    stopAtHandlingClause,
                    stopAtCompositionBoundary,
                    visitedLocals,
                    out matchedMethod);
            }

            matchedMethod = null;
            return false;
        }

        if (!IsKnownClauseFree(inner, context, knownTypes, visitedLocals))
        {
            return FindInPipeline(
                inner,
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod);
        }

        if (FindInPipeline(
            outer,
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

        return IsKnownClauseFree(outer, context, knownTypes, visitedLocals)
            && FindInPipeline(
                inner,
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod);
    }

    private static bool FindInComposeAmbient(
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

        var ambientClauses = new SyntaxNode?[operands.Count];
        var allKnown = true;
        for (var index = 0; index < operands.Count; index++)
        {
            if (!TryGetAmbientClause(
                operands[index],
                context,
                knownTypes,
                visitedLocals,
                out ambientClauses[index]))
            {
                allKnown = false;
                break;
            }
        }

        if (allKnown)
        {
            SyntaxNode? resultAmbient = null;
            for (var index = ambientClauses.Length - 1; index >= 0; index--)
            {
                if (ambientClauses[index] is not null)
                {
                    resultAmbient = ambientClauses[index];
                    break;
                }
            }

            for (var index = 0; index < operands.Count; index++)
            {
                if (SameAmbient(ambientClauses[index], resultAmbient)
                    && FindInPipeline(
                        operands[index],
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

        for (var index = operands.Count - 1; index >= 0; index--)
        {
            if (IsKnownClauseFree(operands[index], context, knownTypes, visitedLocals))
            {
                continue;
            }

            return FindInPipeline(
                operands[index],
                context,
                predicate,
                knownTypes,
                stopAtHandlingClause,
                stopAtCompositionBoundary,
                visitedLocals,
                out matchedMethod);
        }

        foreach (var operand in operands)
        {
            if (FindInPipeline(
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
            var inner = GetArgument(invocation, "inner");
            if (!TryGetAmbientClause(inner, context, knownTypes, visitedLocals, out ambientClause))
            {
                return false;
            }

            return ambientClause is not null
                || TryGetAmbientClause(
                    GetReceiver(invocation),
                    context,
                    knownTypes,
                    visitedLocals,
                    out ambientClause);
        }

        if (method.Name == "Compose")
        {
            var operands = new List<IOperation>();
            foreach (var argument in invocation.Arguments)
            {
                CollectCompositionOperands(argument.Value, context, operands, visitedLocals);
            }

            ambientClause = null;
            foreach (var operand in operands)
            {
                if (!TryGetAmbientClause(
                    operand,
                    context,
                    knownTypes,
                    visitedLocals,
                    out var operandAmbient))
                {
                    ambientClause = null;
                    return false;
                }

                ambientClause = operandAmbient ?? ambientClause;
            }

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

        var scope = (SyntaxNode?)declarator.FirstAncestorOrSelf<BlockSyntax>()
            ?? declarator.SyntaxTree.GetRoot(context.CancellationToken);

        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText == local.Name
                && SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol,
                    local)
                && (IsWritten(identifier)
                    || local.Type is IArrayTypeSymbol
                        && IsEscapingArrayReference(identifier, localReference.Syntax)))
            {
                initializer = null;
                return false;
            }
        }

        initializer = semanticModel.GetOperation(initializerSyntax, context.CancellationToken);
        return initializer is not null;
    }

    private static bool TryGetSingleUseInitializer(
        ILocalReferenceOperation localReference,
        OperationAnalysisContext context,
        out IOperation? initializer)
    {
        if (!TryGetStableInitializer(localReference, context, out initializer))
        {
            return false;
        }

        var declaration = localReference.Local.DeclaringSyntaxReferences[0]
            .GetSyntax(context.CancellationToken);
        var declarationScope = GetExecutableScope(declaration, context.CancellationToken);
        var referenceScope = GetExecutableScope(localReference.Syntax, context.CancellationToken);
        if (!SameSyntax(declarationScope, referenceScope))
        {
            initializer = null;
            return false;
        }

        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel is null)
        {
            initializer = null;
            return false;
        }

        var referenceCount = 0;
        foreach (var identifier in declarationScope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText == localReference.Local.Name
                && SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol,
                    localReference.Local)
                && ++referenceCount > 1)
            {
                initializer = null;
                return false;
            }
        }

        return referenceCount == 1;
    }

    private static SyntaxNode GetExecutableScope(SyntaxNode syntax, CancellationToken cancellationToken)
    {
        for (SyntaxNode? current = syntax; current is not null; current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax
                or LocalFunctionStatementSyntax
                or BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax)
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
        return method.Name is "Execute" or "ExecuteAsync" or "ExecuteOutcomeAsync"
            && (knownTypes.IsShield(method.ContainingType)
                || knownTypes.IsShieldTaskExtensions(method.ContainingType));
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
                    && attributeType.ContainingNamespace.ToDisplayString() is "TUnit.Core"
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
        (method.Name is "When" or "WhenResult" or "WhenDefault")
        && (knownTypes.IsShield(method.ContainingType)
            || knownTypes.IsShieldBuilder(method.ContainingType)
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
            var assemblyName = compilation.AssemblyName;
            IsTestAssembly = assemblyName is not null
                && (assemblyName.EndsWith(".Test", StringComparison.OrdinalIgnoreCase)
                    || assemblyName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));
        }

        internal bool IsTestAssembly { get; }

        internal bool IsShield(INamedTypeSymbol type) =>
            Is(type, _shield) || Is(type, _shieldOfT);

        internal bool IsShieldBuilder(INamedTypeSymbol type) =>
            Is(type, _shieldBuilder) || Is(type, _shieldBuilderOfT);

        internal bool IsShieldExtensions(INamedTypeSymbol type) => Is(type, _shieldExtensions);

        internal bool IsShieldTaskExtensions(INamedTypeSymbol type) => Is(type, _shieldTaskExtensions);

        internal bool IsPartitionedShield(INamedTypeSymbol type) =>
            Is(type, _partitionedShield) || Is(type, _partitionedShieldOfT);

        private static bool Is(INamedTypeSymbol type, INamedTypeSymbol? expected) =>
            expected is not null
            && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, expected);
    }
}
