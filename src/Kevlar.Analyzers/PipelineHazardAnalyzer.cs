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
/// void fallbacks used for result-returning executions, hedging on an untyped shield, handling
/// clauses that never reach a reactive strategy, and fluent chaining results discarded as statements.
/// It also reports, at hint severity, the strategies that inherit a handling clause declared
/// earlier in their chain, so the clause's span is visible where it applies, and the default-result
/// clauses written for a value type, whose default is usually a legitimate result, plus reactive
/// strategies that implicitly accept default handling.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PipelineHazardAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The KEV002 rule.</summary>
    public static readonly DiagnosticDescriptor SynchronousHedgeRule = new(
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
        description: "A shield containing a void fallback rejects every result-returning execution at the execution boundary, before the delegate or any strategy runs.");

    /// <summary>The KEV006 rule.</summary>
    public static readonly DiagnosticDescriptor UntypedHedgeRule = new(
        id: "KEV006",
        title: "Hedging on an untyped Shield requires an idempotent action",
        messageFormat: "Hedging on an untyped Shield runs the execution delegate more than once, concurrently. Build a result-aware shield with Shield.For<T>() so result clauses can select the winning attempt, or confirm the action is idempotent.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An untyped Shield can only judge hedged attempts by their exceptions, so every attempt it launches runs to completion against the real dependency. Duplicate writes, charges, or sends from a losing hedge are observable unless the action is idempotent.");

    /// <summary>The KEV007 rule.</summary>
    public static readonly DiagnosticDescriptor DeadHandlingClauseRule = new(
        id: "KEV007",
        title: "Handling clause never reaches a reactive strategy",
        messageFormat: "This handling clause never reaches a reactive strategy, so it has no effect: {0}. Finish the clause with Retry, CircuitBreaker, Hedge, Fallback, or Use, or remove it.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A When/Or clause only changes behaviour once a reactive strategy consumes it. A clause whose builder is discarded, or that a later When clause replaces before any reactive strategy is added, silently does nothing.");

    /// <summary>The KEV008 rule.</summary>
    public static readonly DiagnosticDescriptor DiscardedChainResultRule = new(
        id: "KEV008",
        title: "Fluent chaining result is discarded",
        messageFormat: "'{0}' returns a new shield instead of changing this one, and its result is discarded here, so this statement configures nothing. Assign the returned shield, or continue the chain from it.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Shield, Shield<TResult> and their builders are immutable: every fluent method returns a new instance and leaves its receiver untouched. A chaining call written as a statement therefore has no effect.");

    /// <summary>The KEV009 rule.</summary>
    public static readonly DiagnosticDescriptor InheritedHandlingClauseRule = new(
        id: "KEV009",
        title: "Strategy inherits a handling clause declared earlier in the chain",
        messageFormat: "This strategy inherits the handling clause declared earlier in the chain ('{0}'); only those exceptions or results count toward it. Declare a new clause, or call 'WhenAnyError()' first, to give it different handling.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A handling clause stays ambient for every reactive strategy chained after it until a new clause replaces it, WhenAnyError resets it, or Wrap/Compose seals it. That is by design; this diagnostic makes the inherited span visible at the strategies that silently pick the clause up.");

    /// <summary>The KEV010 rule.</summary>
    public static readonly DiagnosticDescriptor DefaultResultClauseOnValueTypeRule = new(
        id: "KEV010",
        title: "Default-result clause handles a value type's default",
        messageFormat: "'{0}' handles 'default({1})', which for a value type — 0, false, an empty struct — is as often a legitimate result as a failure. Confirm that is intended, or select the failing results with 'WhenResult'/'OrResult'.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "WhenResultIsDefault and OrResultIsDefault were named for reference types, where default(TResult) is null and a missing value is usually the failure. On a value type the same clause treats a zero, a false, or an empty struct as a failure worth retrying, hedging, or falling back from. Reference-type shields can say so explicitly with WhenResultIsNull and OrResultIsNull.");

    /// <summary>The KEV011 rule.</summary>
    public static readonly DiagnosticDescriptor ImplicitDefaultHandlingRule = new(
        id: "KEV011",
        title: "Reactive strategy uses implicit default handling",
        messageFormat: "'{0}' uses Kevlar's default handling, which includes programming errors. Declare a When clause or local HandlesException override when only expected failures should be handled.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Without an explicit handling clause, reactive strategies handle ordinary exceptions, including programming errors such as ArgumentException and InvalidOperationException. This hint makes that implicit policy visible so transient-failure pipelines can narrow it deliberately.");

    /// <summary>The KEV012 rule.</summary>
    public static readonly DiagnosticDescriptor AsyncConfigurationWithSynchronousExecuteRule = new(
        id: "KEV012",
        title: "Asynchronous strategy configuration requires ExecuteAsync",
        messageFormat: "This shield configures '{0}', which cannot run through synchronous 'Execute'. Use 'ExecuteAsync' or configure its synchronous counterpart.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Asynchronous callbacks and generators can capture a SynchronizationContext. Kevlar rejects synchronous Execute for statically known asynchronous strategy configuration instead of blocking the calling thread.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            SynchronousHedgeRule,
            UnreachableReactiveStrategyRule,
            EphemeralStatefulShieldRule,
            VoidFallbackResultExecutionRule,
            UntypedHedgeRule,
            DeadHandlingClauseRule,
            DiscardedChainResultRule,
            InheritedHandlingClauseRule,
            DefaultResultClauseOnValueTypeRule,
            ImplicitDefaultHandlingRule,
            AsyncConfigurationWithSynchronousExecuteRule);

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

        if (IsSynchronousExecute(invocation.TargetMethod, knownTypes))
        {
            string? asyncMember = null;
            if (FindInPipeline(
                    GetReceiver(invocation),
                    context,
                    candidate => TryFindAsyncConfiguration(candidate, out asyncMember),
                    knownTypes,
                    stopAtHandlingClause: false,
                    stopAtCompositionBoundary: false,
                    out _))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    AsyncConfigurationWithSynchronousExecuteRule,
                    invocation.Syntax.GetLocation(),
                    asyncMember!));
            }
        }

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
                SynchronousHedgeRule,
                invocation.Syntax.GetLocation()));
        }

        if (IsFallbackMethod(invocation.TargetMethod, knownTypes)
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

        if (IsUntypedHedge(invocation, knownTypes))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UntypedHedgeRule,
                invocation.Syntax.GetLocation()));
        }

        if (TryFindDeadHandlingClause(invocation, context, knownTypes, out var deadClauseReason))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DeadHandlingClauseRule,
                invocation.Syntax.GetLocation(),
                deadClauseReason));
        }

        if (IsDiscardedChainResult(invocation, knownTypes))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiscardedChainResultRule,
                invocation.Syntax.GetLocation(),
                Normalize(invocation.TargetMethod).Name));
        }

        if (InheritsAmbientHandlingClause(invocation, context, knownTypes, out var inheritedClause))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InheritedHandlingClauseRule,
                GetMethodNameLocation(invocation),
                inheritedClause));
        }

        if (IsDefaultResultClauseOnValueType(invocation, knownTypes, out var clauseMethod, out var resultType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DefaultResultClauseOnValueTypeRule,
                GetMethodNameLocation(invocation),
                clauseMethod,
                resultType));
        }

        if (UsesImplicitDefaultHandling(invocation, context, knownTypes))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ImplicitDefaultHandlingRule,
                GetMethodNameLocation(invocation),
                Normalize(invocation.TargetMethod).Name));
        }
    }

    private static bool UsesImplicitDefaultHandling(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        KnownTypes knownTypes)
    {
        var method = Normalize(invocation.TargetMethod);
        if (!IsClauseConsumingStrategy(method, knownTypes)
            || HasLocalHandlingOverride(invocation, context) is not false
            || !TryGetAmbientClause(invocation, context, knownTypes, visitedLocals: null, out var clause)
            || clause is not null)
        {
            return false;
        }

        return !HasExplicitDefaultResetInCurrentSegment(
            GetReceiver(invocation),
            context,
            knownTypes,
            visitedLocals: null);
    }

    private static bool HasExplicitDefaultResetInCurrentSegment(
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

            var found = HasExplicitDefaultResetInCurrentSegment(
                initializer,
                context,
                knownTypes,
                visitedLocals);
            visitedLocals.Remove(localReference.Local);
            return found;
        }

        if (operation is IConditionalAccessOperation conditionalAccess)
        {
            return HasExplicitDefaultResetInCurrentSegment(
                conditionalAccess.Operation,
                context,
                knownTypes,
                visitedLocals);
        }

        if (operation is not IInvocationOperation invocation)
        {
            return false;
        }

        var method = Normalize(invocation.TargetMethod);
        if (IsCompositionBoundary(method, knownTypes))
        {
            return false;
        }

        if (method.Name == "WhenAnyError" && StartsHandlingClause(method, knownTypes))
        {
            return true;
        }

        return IsKevlarFluentMethod(method, knownTypes)
            && HasExplicitDefaultResetInCurrentSegment(
                GetReceiver(invocation),
                context,
                knownTypes,
                visitedLocals);
    }

    /// <summary>
    /// Reports a <c>WhenResultIsDefault</c>/<c>OrResultIsDefault</c> clause whose result is a
    /// non-nullable value type. Type parameters are left alone: generic code cannot say which
    /// results its callers consider failures, and <c>default(TResult)</c> is the only term it has.
    /// </summary>
    private static bool IsDefaultResultClauseOnValueType(
        IInvocationOperation invocation,
        KnownTypes knownTypes,
        out string? clauseMethod,
        out string? resultType)
    {
        clauseMethod = null;
        resultType = null;

        var method = Normalize(invocation.TargetMethod);
        if (method.Name is not ("WhenResultIsDefault" or "OrResultIsDefault")
            || !IsKevlarFluentMethod(method, knownTypes))
        {
            return false;
        }

        // Read TResult from the constructed receiver: the normalized definition still says TResult.
        var typeArguments = invocation.TargetMethod.ContainingType.TypeArguments;
        if (typeArguments.Length != 1 || !IsNonNullableValueType(typeArguments[0]))
        {
            return false;
        }

        clauseMethod = method.Name;
        resultType = typeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return true;
    }

    /// <summary>
    /// Whether the result type has a <c>default</c> that is an ordinary value. <c>Nullable&lt;T&gt;</c>
    /// is excluded: its default is the missing value the clause was written for.
    /// </summary>
    private static bool IsNonNullableValueType(ITypeSymbol type) =>
        type.IsValueType
        && type.TypeKind != TypeKind.TypeParameter
        && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;

    /// <summary>
    /// Reports the second and later reactive strategies that read one ambient handling clause.
    /// The first strategy after a <c>When…</c> states its own handling at the call site; the ones
    /// after it inherit it silently, which is what this walk surfaces.
    /// </summary>
    private static bool InheritsAmbientHandlingClause(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        out string? clause)
    {
        clause = null;
        if (!IsClauseConsumingStrategy(Normalize(invocation.TargetMethod), knownTypes)
            || HasLocalHandlingOverride(invocation, context) is not false)
        {
            return false;
        }

        HashSet<ISymbol>? visitedLocals = null;
        var sawEarlierStrategy = false;
        for (var current = Unwrap(GetReceiver(invocation)); current is not null;)
        {
            if (current is ILocalReferenceOperation localReference)
            {
                visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                if (!visitedLocals.Add(localReference.Local)
                    || !TryGetStableInitializer(localReference, context, out var initializer))
                {
                    return false;
                }

                current = Unwrap(initializer);
                continue;
            }

            if (current is IConditionalAccessOperation conditionalAccess)
            {
                current = Unwrap(conditionalAccess.Operation);
                continue;
            }

            if (current is not IInvocationOperation link)
            {
                return false;
            }

            var method = Normalize(link.TargetMethod);

            // WhenAnyError resets the clause and Wrap/Compose seals it, so nothing is inherited
            // across either. Checked before StartsHandlingClause, which also matches WhenAnyError.
            if (method.Name == "WhenAnyError" || IsCompositionBoundary(method, knownTypes))
            {
                return false;
            }

            if (StartsHandlingClause(method, knownTypes))
            {
                if (!sawEarlierStrategy)
                {
                    return false;
                }

                clause = DescribeClause(link);
                return true;
            }

            if (!IsKevlarFluentMethod(method, knownTypes))
            {
                return false;
            }

            // Proactive strategies carry no clause and Or… only extends the one being walked to,
            // so neither counts as an earlier consumer.
            if (IsClauseConsumingStrategy(method, knownTypes)
                && HasLocalHandlingOverride(link, context) is false)
            {
                sawEarlierStrategy = true;
            }

            current = Unwrap(GetReceiver(link));
        }

        return false;
    }

    /// <summary>
    /// The built-in reactive strategies that read the ambient clause rather than declaring handling.
    /// <c>Use</c> is excluded: its factory takes the <c>HandlingClause</c> as a parameter, so what it
    /// inherits is already spelled out at the call site.
    /// </summary>
    private static bool IsClauseConsumingStrategy(IMethodSymbol method, KnownTypes knownTypes) =>
        IsFallbackMethod(method, knownTypes)
        || ((method.Name is "Retry" or "RetryForever" or "Hedge" or "CircuitBreaker")
            && IsKevlarFluentMethod(method, knownTypes));

    private static bool IsFallbackMethod(IMethodSymbol method, KnownTypes knownTypes) =>
        IsKevlarFluentMethod(method, knownTypes, "Fallback")
        || IsKevlarFluentMethod(method, knownTypes, "FallbackTo");

    /// <summary>Renders a clause declaration the way it was written, e.g. <c>When&lt;HttpRequestException&gt;…</c>.</summary>
    private static string DescribeClause(IInvocationOperation invocation) =>
        (invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
            ? memberAccess.Name.ToString()
            : Normalize(invocation.TargetMethod).Name) + "…";

    /// <summary>
    /// The invoked method's name alone, so a hint marks the one strategy it is about rather than
    /// underlining the whole fluent chain leading up to it.
    /// </summary>
    private static Location GetMethodNameLocation(IInvocationOperation invocation) =>
        invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
            ? memberAccess.Name.GetLocation()
            : invocation.Syntax.GetLocation();

    /// <summary>
    /// Reports a fluent call whose new shield is dropped where it stands. Calls that hand back a
    /// clause builder are left to KEV007, which names that hazard precisely.
    /// </summary>
    private static bool IsDiscardedChainResult(IInvocationOperation invocation, KnownTypes knownTypes)
    {
        var method = Normalize(invocation.TargetMethod);
        return IsKevlarFluentMethod(method, knownTypes)
            && method.ReturnType is INamedTypeSymbol returnType
            && knownTypes.IsShield(returnType)
            && IsExpressionStatement(invocation);
    }

    private static bool IsExpressionStatement(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                case IConversionOperation or IParenthesizedOperation:
                    continue;

                case IExpressionStatementOperation:
                    return true;

                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Reports a handling clause that cannot change any strategy's behaviour: either the
    /// <c>ShieldBuilder</c> it produces is dropped without a strategy, or a later <c>When…</c> in
    /// the same fluent chain replaces it before a reactive strategy consumes it.
    /// </summary>
    private static bool TryFindDeadHandlingClause(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        out string? reason)
    {
        var method = Normalize(invocation.TargetMethod);

        if (ProducesHandlingClauseBuilder(method, knownTypes)
            && IsDiscardedClauseBuilder(invocation, context))
        {
            reason = "the ShieldBuilder it returns is discarded";
            return true;
        }

        if (DeclaresHandlingClause(method, knownTypes)
            && IsReplacedBeforeUse(invocation, knownTypes))
        {
            reason = "a later When clause replaces it first";
            return true;
        }

        reason = null;
        return false;
    }

    /// <summary>Whether the fluent method hands back a clause builder that still needs a strategy.</summary>
    private static bool ProducesHandlingClauseBuilder(IMethodSymbol method, KnownTypes knownTypes) =>
        method.ReturnType is INamedTypeSymbol returnType
        && knownTypes.IsShieldBuilder(returnType)
        && IsKevlarFluentMethod(method, knownTypes);

    /// <summary>Whether the method opens a clause carrying predicates, as opposed to resetting one.</summary>
    private static bool DeclaresHandlingClause(IMethodSymbol method, KnownTypes knownTypes) =>
        method.Name != "WhenAnyError" && StartsHandlingClause(method, knownTypes);

    /// <summary>Whether a reactive strategy reads the ambient clause this method seals.</summary>
    private static bool ConsumesHandlingClause(IMethodSymbol method, KnownTypes knownTypes) =>
        IsClauseConsumingStrategy(method, knownTypes)
        || (IsKevlarFluentMethod(method, knownTypes, "Use")
            && method.Parameters.Length == 1
            && method.Parameters[0].Type.TypeKind == TypeKind.Delegate);

    private static bool IsDiscardedClauseBuilder(
        IInvocationOperation invocation,
        OperationAnalysisContext context)
    {
        for (var parent = invocation.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                case IConversionOperation or IParenthesizedOperation:
                    continue;

                // `shield.When<T>();` — the builder is dropped where it stands.
                case IExpressionStatementOperation:
                    return true;

                // `_ = shield.When<T>();` — dropped just as deliberately.
                case IAssignmentOperation { Target: IDiscardOperation }:
                    return true;

                // `var clause = shield.When<T>();` with no later mention of `clause`.
                case IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator }:
                    return IsNeverRead(declarator.Symbol, context);

                // A chained call consumes it: either another Or… (analysed in its own right, so
                // only the outermost link of a dead chain is reported) or a strategy that seals it.
                // Anything else — an argument, a return, a field — escapes this analysis.
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool IsNeverRead(ILocalSymbol local, OperationAnalysisContext context)
    {
        var declarations = local.DeclaringSyntaxReferences;
        var semanticModel = context.Operation.SemanticModel;
        if (declarations.Length != 1 || semanticModel is null)
        {
            return false;
        }

        var declarator = declarations[0].GetSyntax(context.CancellationToken);
        if (semanticModel.SyntaxTree != declarator.SyntaxTree)
        {
            return false;
        }

        var scope = GetExecutableScope(declarator, context.CancellationToken);
        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText == local.Name
                && SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol,
                    local))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Walks outward from a <c>When…</c> through the calls that consume its value. Only a chain
    /// that reaches another clause declaration — with nothing reactive in between — is dead;
    /// anything the walk cannot follow leaves the clause alone.
    /// </summary>
    private static bool IsReplacedBeforeUse(IInvocationOperation invocation, KnownTypes knownTypes)
    {
        for (var consumer = FindChainedConsumer(invocation); consumer is not null; consumer = FindChainedConsumer(consumer))
        {
            var method = Normalize(consumer.TargetMethod);

            if (ConsumesHandlingClause(method, knownTypes))
            {
                return false;
            }

            // A new clause replaced the old one, and nothing reactive read it on the way here.
            // Checked before the builder test because When… also returns a builder; only the
            // Or… continuations declared on ShieldBuilder itself extend the existing clause.
            if (StartsHandlingClause(method, knownTypes))
            {
                return true;
            }

            // Or… continues the same clause.
            if (ProducesHandlingClauseBuilder(method, knownTypes))
            {
                continue;
            }

            // Proactive strategies and metadata keep the clause ambient; anything else — including
            // Wrap and Compose, which seal clauses — is beyond what this walk should judge.
            if (IsCompositionBoundary(method, knownTypes) || !IsKevlarFluentMethod(method, knownTypes))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>The invocation that takes <paramref name="invocation"/> as its fluent receiver, if any.</summary>
    private static IInvocationOperation? FindChainedConsumer(IInvocationOperation invocation)
    {
        for (IOperation? current = invocation, parent = invocation.Parent;
            parent is not null;
            current = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                case IConversionOperation or IParenthesizedOperation:
                    continue;

                // An instance call: `clause.Retry(3)`.
                case IInvocationOperation consumer
                    when ReferenceEquals(GetReceiver(consumer), current):
                    return consumer;

                // A reduced extension call: `shield.When<T>()` parents the receiver under the
                // argument that carries it, not under the invocation itself.
                case IArgumentOperation { Parent: IInvocationOperation extensionConsumer }
                    when ReferenceEquals(GetReceiver(extensionConsumer), current):
                    return extensionConsumer;

                default:
                    return null;
            }
        }

        return null;
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

        if (method.Name is "GetShield" or "GetShieldAsync"
            && knownTypes.IsPartitionedShield(method.ContainingType))
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

        if (operation is IInvocationOperation invocation
            && Normalize(invocation.TargetMethod) is { Name: "CreateAsync" } factory
            && knownTypes.IsPartitionedShield(factory.ContainingType))
        {
            statefulComponent = "PartitionedShield";
            location = invocation.Syntax.GetLocation();
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
                candidate => IsFallbackMethod(candidate.TargetMethod, knownTypes)
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
                case IAwaitOperation awaitOperation:
                    operation = awaitOperation.Operation;
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
        return (method.Name is "Execute" or "ExecuteAsync" or "ExecuteOutcome" or "ExecuteOutcomeAsync"
                or "ExecuteWithContext" or "ExecuteWithContextAsync")
            && (knownTypes.IsShield(method.ContainingType)
                || knownTypes.IsShieldTaskExtensions(method.ContainingType));
    }

    private static bool IsResultReturningExecution(IMethodSymbol method, KnownTypes knownTypes)
    {
        method = Normalize(method);
        return IsExecution(method, knownTypes)
            && !method.ReturnsVoid
            && !knownTypes.IsNonGenericExecutionResult(method.ReturnType);
    }

    private static bool IsVoidFallback(IMethodSymbol method, KnownTypes knownTypes)
    {
        method = Normalize(method);
        return method.Name == "Fallback"
            && method.ReturnType is INamedTypeSymbol returnType
            && knownTypes.IsUntypedShield(returnType)
            && IsKevlarFluentMethod(method, knownTypes);
    }

    private static bool IsUntypedHedge(IInvocationOperation invocation, KnownTypes knownTypes)
    {
        var method = Normalize(invocation.TargetMethod);
        return method.Name == "Hedge"
            && method.ReturnType is INamedTypeSymbol returnType
            && knownTypes.IsUntypedShield(returnType)
            && IsKevlarFluentMethod(method, knownTypes)
            && (!TryGetConstantMaxHedgedAttempts(invocation, out var maxHedgedAttempts)
                || maxHedgedAttempts > 0);
    }

    private static bool IsStatefulStrategy(IMethodSymbol method, KnownTypes knownTypes) =>
        method.Name is "CircuitBreaker" or "RateLimit" or "ConcurrencyLimit" or "UseRateLimiter"
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
        return method.Name is "Execute" or "ExecuteOutcome" or "ExecuteWithContext"
            && knownTypes.IsShield(method.ContainingType);
    }

    private static bool TryFindAsyncConfiguration(
        IInvocationOperation invocation,
        out string? memberName)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == "configure"
                && TryFindAsyncConfiguration(argument.Value, out memberName))
            {
                return true;
            }
        }

        memberName = null;
        return false;
    }

    private static bool TryFindAsyncConfiguration(
        IOperation operation,
        out string? memberName)
    {
        operation = Unwrap(operation)!;
        if (operation is IDelegateCreationOperation delegateCreation)
        {
            return TryFindAsyncConfiguration(
                delegateCreation.Target,
                allowAnonymousFunction: true,
                out memberName);
        }

        return TryFindAsyncConfiguration(operation, allowAnonymousFunction: true, out memberName);
    }

    private static bool TryFindAsyncConfiguration(
        IOperation operation,
        bool allowAnonymousFunction,
        out string? memberName)
    {
        operation = Unwrap(operation)!;
        if (operation is IAnonymousFunctionOperation && !allowAnonymousFunction)
        {
            memberName = null;
            return false;
        }

        if (operation is IAssignmentOperation
            {
                Target: IPropertyReferenceOperation propertyReference,
                Value: { } value,
            }
            && IsAsyncConfigurationProperty(propertyReference.Property)
            && value.ConstantValue is not { HasValue: true, Value: null })
        {
            memberName = $"{propertyReference.Property.ContainingType.Name}.{propertyReference.Property.Name}";
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (TryFindAsyncConfiguration(
                    child,
                    allowAnonymousFunction: false,
                    out memberName))
            {
                return true;
            }
        }

        memberName = null;
        return false;
    }

    private static bool IsAsyncConfigurationProperty(IPropertySymbol property) =>
        property.ContainingNamespace.ToDisplayString().StartsWith("Kevlar", StringComparison.Ordinal)
        && property.Name is
            "OnRetryAsync"
            or "DelayGeneratorAsync"
            or "OnTimeoutAsync"
            or "TimeoutGenerator"
            or "OnFallbackAsync"
            or "OnStateChangedAsync"
            or "BreakDurationGenerator"
            or "OnRejectedAsync";

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
            && propertyReference.Property.Name is
                "HandlesException"
                or "HandlesResult"
                or "HandlesExceptionWithContext"
                or "HandlesResultWithContext"
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
                    || current.GetMembers("HandlesResult").Length > 0
                    || current.GetMembers("HandlesExceptionWithContext").Length > 0
                    || current.GetMembers("HandlesResultWithContext").Length > 0))
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

        return TryGetConstantMaxHedgedAttempts(invocation, out var maxHedgedAttempts)
            ? maxHedgedAttempts > 0
            : invocation.Arguments.Any(static argument => argument.Parameter?.Name == "configure");
    }

    private static bool TryGetConstantMaxHedgedAttempts(
        IInvocationOperation invocation,
        out int maxHedgedAttempts)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == "maxHedgedAttempts"
                && argument.Value.ConstantValue is { HasValue: true, Value: int value })
            {
                maxHedgedAttempts = value;
                return true;
            }

            if (argument.Parameter?.Name == "configure"
                && TryGetConfiguredMaxHedgedAttempts(argument.Value, out maxHedgedAttempts))
            {
                return true;
            }
        }

        maxHedgedAttempts = default;
        return false;
    }

    private static bool TryGetConfiguredMaxHedgedAttempts(
        IOperation operation,
        out int maxHedgedAttempts)
    {
        operation = Unwrap(operation)!;
        if (operation is IDelegateCreationOperation delegateCreation)
        {
            operation = Unwrap(delegateCreation.Target)!;
        }

        if (operation is not IAnonymousFunctionOperation anonymousFunction)
        {
            maxHedgedAttempts = default;
            return false;
        }

        if (anonymousFunction.Symbol.IsAsync)
        {
            maxHedgedAttempts = default;
            return false;
        }

        maxHedgedAttempts = 1;
        var found = true;
        return AnalyzeHedgeConfigurator(
            anonymousFunction.Body,
            anonymousFunction.Symbol.Parameters[0],
            ref found,
            ref maxHedgedAttempts)
            && found;
    }

    private static bool AnalyzeHedgeConfigurator(
        IOperation operation,
        IParameterSymbol configuratorParameter,
        ref bool found,
        ref int maxHedgedAttempts)
    {
        operation = Unwrap(operation)!;
        if (operation is ISimpleAssignmentOperation assignment)
        {
            if (IsConfiguredHedgeAttemptProperty(assignment.Target, configuratorParameter))
            {
                if (ContainsParameterReference(assignment.Value, configuratorParameter)
                    || assignment.Value.ConstantValue is not { HasValue: true, Value: int value })
                {
                    return false;
                }

                maxHedgedAttempts = value;
                found = true;
                return true;
            }

            if ((!IsDirectConfiguratorProperty(assignment.Target, configuratorParameter)
                    && ContainsParameterReference(assignment.Target, configuratorParameter))
                || ContainsParameterReference(assignment.Value, configuratorParameter))
            {
                return false;
            }
        }

        if (operation is IVariableInitializerOperation initializer
            && ContainsParameterReference(initializer.Value, configuratorParameter))
        {
            return false;
        }

        if (operation is IDeconstructionAssignmentOperation deconstruction
            && ContainsParameterReference(deconstruction.Value, configuratorParameter))
        {
            return false;
        }

        if (operation is ICompoundAssignmentOperation compoundAssignment
            && IsConfiguredHedgeAttemptProperty(compoundAssignment.Target, configuratorParameter))
        {
            return false;
        }

        if (operation is IIncrementOrDecrementOperation increment
            && IsConfiguredHedgeAttemptProperty(increment.Target, configuratorParameter))
        {
            return false;
        }

        if (operation is IAnonymousFunctionOperation)
        {
            return true;
        }

        if (operation is IConditionalOperation
            or ICoalesceOperation
            or IConditionalAccessOperation
            or ILoopOperation
            or ISwitchOperation
            or ISwitchExpressionOperation
            or ITryOperation
            or IInvocationOperation
            or IObjectCreationOperation
            or ILocalFunctionOperation
            or IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.ConditionalAnd
                    or BinaryOperatorKind.ConditionalOr,
            })
        {
            return false;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (!AnalyzeHedgeConfigurator(
                    child,
                    configuratorParameter,
                    ref found,
                    ref maxHedgedAttempts))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsConfiguredHedgeAttemptProperty(
        IOperation target,
        IParameterSymbol configuratorParameter) =>
        target is IPropertyReferenceOperation
        {
            Property:
            {
                Name: "MaxHedgedAttempts",
                ContainingType.Name: "HedgeOptions",
            } property,
            Instance: { } instance,
        }
        && property.ContainingNamespace.ToDisplayString() == "Kevlar"
        && ReferencesParameter(instance, configuratorParameter);

    private static bool IsDirectConfiguratorProperty(
        IOperation target,
        IParameterSymbol configuratorParameter) =>
        target is IPropertyReferenceOperation { Instance: { } instance }
        && ReferencesParameter(instance, configuratorParameter);

    private static bool ReferencesParameter(
        IOperation operation,
        IParameterSymbol configuratorParameter) =>
        Unwrap(operation) is IParameterReferenceOperation parameterReference
        && SymbolEqualityComparer.Default.Equals(
            parameterReference.Parameter,
            configuratorParameter);

    private static bool ContainsParameterReference(
        IOperation operation,
        IParameterSymbol configuratorParameter)
    {
        if (ReferencesParameter(operation, configuratorParameter))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (ContainsParameterReference(child, configuratorParameter))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StartsHandlingClause(IMethodSymbol method, KnownTypes knownTypes) =>
        (method.Name is "When" or "WhenContext" or "WhenResult" or "WhenResultContext"
            or "WhenResultIsDefault" or "WhenResultIsNull" or "WhenAnyError")
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
                || knownTypes.IsShieldExtensions(method.ContainingType)
                || knownTypes.IsShieldRateLimiterExtensions(method.ContainingType));
    }

    private sealed class KnownTypes
    {
        private readonly INamedTypeSymbol? _shield;
        private readonly INamedTypeSymbol? _shieldOfT;
        private readonly INamedTypeSymbol? _shieldBuilder;
        private readonly INamedTypeSymbol? _shieldBuilderOfT;
        private readonly INamedTypeSymbol? _shieldExtensions;
        private readonly INamedTypeSymbol? _shieldResultExtensions;
        private readonly INamedTypeSymbol? _shieldTaskExtensions;
        private readonly INamedTypeSymbol? _shieldRateLimiterExtensions;
        private readonly INamedTypeSymbol? _partitionedShield;
        private readonly INamedTypeSymbol? _partitionedShieldOfT;
        private readonly INamedTypeSymbol? _outcome;
        private readonly INamedTypeSymbol? _task;
        private readonly INamedTypeSymbol? _taskOfT;
        private readonly INamedTypeSymbol? _valueTask;
        private readonly INamedTypeSymbol? _valueTaskOfT;

        internal KnownTypes(Compilation compilation)
        {
            _shield = compilation.GetTypeByMetadataName("Kevlar.Shield");
            _shieldOfT = compilation.GetTypeByMetadataName("Kevlar.Shield`1");
            _shieldBuilder = compilation.GetTypeByMetadataName("Kevlar.ShieldBuilder");
            _shieldBuilderOfT = compilation.GetTypeByMetadataName("Kevlar.ShieldBuilder`1");
            _shieldExtensions = compilation.GetTypeByMetadataName("Kevlar.ShieldExtensions");
            _shieldResultExtensions = compilation.GetTypeByMetadataName("Kevlar.ShieldResultExtensions");
            _shieldTaskExtensions = compilation.GetTypeByMetadataName("Kevlar.ShieldTaskExtensions");
            _shieldRateLimiterExtensions = compilation.GetTypeByMetadataName(
                "Kevlar.Extensions.RateLimiting.ShieldRateLimiterExtensions");
            _partitionedShield = compilation.GetTypeByMetadataName("Kevlar.PartitionedShield`1");
            _partitionedShieldOfT = compilation.GetTypeByMetadataName("Kevlar.PartitionedShield`2");
            _outcome = compilation.GetTypeByMetadataName("Kevlar.Outcome");
            _task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
            _taskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
            _valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
            _valueTaskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
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

        internal bool IsShieldExtensions(INamedTypeSymbol type) =>
            Is(type, _shieldExtensions) || Is(type, _shieldResultExtensions);

        internal bool IsShieldTaskExtensions(INamedTypeSymbol type) => Is(type, _shieldTaskExtensions);

        internal bool IsShieldRateLimiterExtensions(INamedTypeSymbol type) =>
            Is(type, _shieldRateLimiterExtensions);

        internal bool IsPartitionedShield(INamedTypeSymbol type) =>
            Is(type, _partitionedShield) || Is(type, _partitionedShieldOfT);

        internal bool IsNonGenericExecutionResult(ITypeSymbol type) =>
            type is INamedTypeSymbol namedType
            && (Is(namedType, _outcome)
                || Is(namedType, _task)
                || Is(namedType, _valueTask)
                || ((Is(namedType, _taskOfT) || Is(namedType, _valueTaskOfT))
                    && IsNonGenericExecutionResult(namedType.TypeArguments[0])));

        private static bool Is(INamedTypeSymbol type, INamedTypeSymbol? expected) =>
            expected is not null
            && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, expected);
    }
}
