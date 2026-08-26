using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
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

    /// <summary>The KEV013 rule.</summary>
    public static readonly DiagnosticDescriptor AsyncWorkInSynchronousCallbackRule = new(
        id: "KEV013",
        title: "Asynchronous work is assigned to a synchronous callback",
        messageFormat: "'{0}' is synchronous, so this asynchronous delegate becomes async void or discards its task. Use {1} instead.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Kevlar's synchronous callback properties cannot await asynchronous work. Async lambdas become async void, and task-returning expression lambdas discard the task, allowing pooled event context to outlive its execution.");

    /// <summary>The KEV014 rule.</summary>
    public static readonly DiagnosticDescriptor DeferredContextCaptureRule = new(
        id: "KEV014",
        title: "Pooled event context is captured by deferred work",
        messageFormat: "This deferred work captures a pooled event context. Copy the values it needs before scheduling the work.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Task.Run and ThreadPool work can execute after a strategy callback completes. Capturing an event context there can observe state from a later execution when the pooled context is reused.");

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
            AsyncConfigurationWithSynchronousExecuteRule,
            AsyncWorkInSynchronousCallbackRule,
            DeferredContextCaptureRule);

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
            compilationContext.RegisterSyntaxNodeAction(
                context => AnalyzeCallbackAssignment(context, knownTypes),
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxKind.AddAssignmentExpression,
                SyntaxKind.CoalesceAssignmentExpression);
        });
    }

    private static void AnalyzeCallbackAssignment(
        SyntaxNodeAnalysisContext context,
        KnownTypes knownTypes)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        var propertyInfo = context.SemanticModel.GetSymbolInfo(
            assignment.Left,
            context.CancellationToken);
        var property = propertyInfo.Symbol as IPropertySymbol
            ?? propertyInfo.CandidateSymbols.OfType<IPropertySymbol>().FirstOrDefault();
        if (property is null || !knownTypes.IsSynchronousCallback(property)
            || !StartsAsynchronousWork(assignment.Right, context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            AsyncWorkInSynchronousCallbackRule,
            assignment.Right.GetLocation(),
            property.Name,
            HasAsyncCallback(property)
                ? $"'{property.Name}Async'"
                : "a synchronous delegate that completes before returning"));

        if (TryFindDiscardedEventContext(
                assignment.Right,
                context,
                knownTypes,
                out var capturedContext))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DeferredContextCaptureRule,
                capturedContext.GetLocation()));
        }
    }

    private static bool HasAsyncCallback(IPropertySymbol property) =>
        property.ContainingType.GetMembers(property.Name + "Async")
            .OfType<IPropertySymbol>()
            .Any();

    private static bool StartsAsynchronousWork(
        ExpressionSyntax expression,
        SyntaxNodeAnalysisContext context)
    {
        var parts = GetCallbackExpressionParts(
                expression,
                context.SemanticModel,
                context.CancellationToken)
            .ToArray();
        if (parts.Length > 0)
        {
            return parts.Any(part => StartsAsynchronousWork(part, context));
        }

        if (expression is AnonymousFunctionExpressionSyntax anonymous)
        {
            if (!anonymous.AsyncKeyword.IsKind(SyntaxKind.None))
            {
                return true;
            }

            if (anonymous is LambdaExpressionSyntax { ExpressionBody: { } body })
            {
                return IsTaskLike(context.SemanticModel.GetTypeInfo(
                    body,
                    context.CancellationToken).Type)
                    || GetCallbackInvocations(
                            body,
                            context.SemanticModel,
                            context.CancellationToken,
                            followTaskReturningLocalFunctions: false)
                        .Any(invocation => IsUnobservedAsyncInvocation(invocation, context));
            }

            return anonymous.Body is BlockSyntax block
                && GetCallbackInvocations(
                        block,
                        context.SemanticModel,
                        context.CancellationToken,
                        followTaskReturningLocalFunctions: false)
                    .Any(invocation => IsUnobservedAsyncInvocation(invocation, context));
        }

        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken);
        if (symbol.Symbol is ILocalSymbol local
            && TryGetStableLocalInitializer(
                local,
                context.SemanticModel,
                context.CancellationToken,
                expression,
                out var initializer))
        {
            return StartsAsynchronousWork(initializer, context);
        }

        return symbol.Symbol is IMethodSymbol method && StartsAsynchronousWork(method)
            || symbol.CandidateSymbols
                .OfType<IMethodSymbol>()
                .Any(static candidate => StartsAsynchronousWork(candidate));
    }

    private static IEnumerable<ExpressionSyntax> GetCallbackExpressionParts(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                yield return parenthesized.Expression;
                break;
            case ConditionalExpressionSyntax conditional:
                yield return conditional.WhenTrue;
                yield return conditional.WhenFalse;
                break;
            case BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.CoalesceExpression)
                    || semanticModel.GetTypeInfo(binary, cancellationToken).Type?.TypeKind
                        == TypeKind.Delegate:
                yield return binary.Left;
                yield return binary.Right;
                break;
            case SwitchExpressionSyntax switchExpression:
                foreach (var arm in switchExpression.Arms)
                {
                    yield return arm.Expression;
                }

                break;
            case CastExpressionSyntax cast:
                yield return cast.Expression;
                break;
            case PostfixUnaryExpressionSyntax postfix
                when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                yield return postfix.Operand;
                break;
            case BaseObjectCreationExpressionSyntax creation
                when semanticModel.GetTypeInfo(creation, cancellationToken).Type?.TypeKind
                    == TypeKind.Delegate
                && creation.ArgumentList is { } arguments:
                foreach (var argument in arguments.Arguments)
                {
                    yield return argument.Expression;
                }

                break;
        }
    }

    private static bool StartsAsynchronousWork(IMethodSymbol method) =>
        (method.IsAsync && method.ReturnsVoid) || IsTaskLike(method.ReturnType);

    private static bool TryFindDiscardedEventContext(
        ExpressionSyntax expression,
        SyntaxNodeAnalysisContext context,
        KnownTypes knownTypes,
        out SyntaxNode capturedContext)
    {
        if (context.SemanticModel.GetSymbolInfo(
                expression,
                context.CancellationToken).Symbol is ILocalSymbol local
            && local.Type.TypeKind == TypeKind.Delegate
            && TryGetStableLocalInitializer(
                local,
                context.SemanticModel,
                context.CancellationToken,
                expression,
                out var initializer))
        {
            return TryFindDiscardedEventContext(
                initializer,
                context,
                knownTypes,
                out capturedContext);
        }

        var parts = GetCallbackExpressionParts(
                expression,
                context.SemanticModel,
                context.CancellationToken)
            .ToArray();
        if (parts.Length > 0)
        {
            foreach (var part in parts)
            {
                if (TryFindDiscardedEventContext(
                    part,
                    context,
                    knownTypes,
                    out capturedContext))
                {
                    return true;
                }
            }

            capturedContext = null!;
            return false;
        }

        if (TryFindAsyncVoidMethodGroupContext(
                expression,
                context,
                knownTypes,
                out capturedContext))
        {
            return true;
        }

        foreach (var anonymous in expression.DescendantNodesAndSelf()
                     .OfType<AnonymousFunctionExpressionSyntax>()
                     .Where(candidate => !candidate.Ancestors()
                         .OfType<AnonymousFunctionExpressionSyntax>()
                         .Any(ancestor => expression.Span.Contains(ancestor.Span))))
        {
            if (TryFindAnonymousFunctionContext(
                anonymous,
                context,
                knownTypes,
                out capturedContext))
            {
                return true;
            }

            foreach (var invocation in GetCallbackInvocations(
                         anonymous.Body,
                         context.SemanticModel,
                         context.CancellationToken,
                         followTaskReturningLocalFunctions: true))
            {
                if (!IsUnobservedAsyncInvocation(invocation, context))
                {
                    continue;
                }

                if (TryGetInvokedStableDelegateInitializer(
                        invocation,
                        context.SemanticModel,
                        context.CancellationToken,
                        out var delegateInitializer)
                    && TryFindEventContextExpression(
                        delegateInitializer,
                        context,
                        knownTypes,
                        out capturedContext))
                {
                    return true;
                }

                if (TryFindPostAwaitMemberContext(
                    invocation,
                    GetRetainedCallbackSymbols(
                        invocation.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>()
                            ?? anonymous,
                        invocation,
                        context,
                        knownTypes),
                    context,
                    knownTypes,
                    out capturedContext))
                {
                    return true;
                }

                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
                    && context.SemanticModel.GetSymbolInfo(
                        invocation,
                        context.CancellationToken).Symbol is IMethodSymbol
                        {
                            ReducedFrom: not null,
                        }
                    && TryFindEventContextExpression(
                        memberAccess.Expression,
                        context,
                        knownTypes,
                        out capturedContext))
                {
                    return true;
                }

                foreach (var argument in invocation.ArgumentList.Arguments)
                {
                    if (TryFindEventContextExpression(
                        argument.Expression,
                        context,
                        knownTypes,
                        out capturedContext))
                    {
                        return true;
                    }
                }
            }
        }

        capturedContext = null!;
        return false;
    }

    private static HashSet<ISymbol> GetRetainedCallbackSymbols(
        AnonymousFunctionExpressionSyntax anonymous,
        InvocationExpressionSyntax invocation,
        SyntaxNodeAnalysisContext context,
        KnownTypes knownTypes)
    {
        var retainedNames = new HashSet<string>(
            GetAnonymousFunctionParameters(anonymous)
                .Select(static parameter => parameter.Identifier.ValueText),
            StringComparer.Ordinal);
        var retainedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var nodes = anonymous.Body.DescendantNodesAndSelf(descendIntoChildren: static node =>
                node is not AnonymousFunctionExpressionSyntax
                    and not LocalFunctionStatementSyntax)
            .ToArray();
        foreach (var alias in nodes
                     .Where(node => node.SpanStart < invocation.SpanStart
                         && node is (VariableDeclaratorSyntax or AssignmentExpressionSyntax))
                     .OrderBy(static node => node.SpanStart))
        {
            var (target, name, value) = alias switch
            {
                VariableDeclaratorSyntax declarator =>
                    (context.SemanticModel.GetDeclaredSymbol(
                            declarator,
                            context.CancellationToken),
                        declarator.Identifier.ValueText,
                        declarator.Initializer?.Value),
                AssignmentExpressionSyntax assignment
                    when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) =>
                    (context.SemanticModel.GetSymbolInfo(
                            assignment.Left,
                            context.CancellationToken).Symbol,
                        GetAssignedName(assignment.Left),
                        assignment.Right),
                _ => (null, null, null),
            };
            if (name is null)
            {
                continue;
            }

            if (value is not null && IsCallbackRetainedExpression(value, retainedNames))
            {
                retainedNames.Add(name);
                if (target is IFieldSymbol or IPropertySymbol)
                {
                    retainedSymbols.Add(target);
                }
            }
            else if (IsUnconditionalAliasWrite(alias, anonymous.Body))
            {
                retainedNames.Remove(name);
            }
        }

        foreach (var assignment in nodes.OfType<AssignmentExpressionSyntax>()
                     .Where(assignment => assignment.SpanStart < invocation.SpanStart
                         && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)))
        {
            var target = context.SemanticModel.GetSymbolInfo(
                assignment.Left,
                context.CancellationToken).Symbol;
            if (target is IFieldSymbol or IPropertySymbol
                && TryFindEventContextExpression(
                    assignment.Right,
                    context,
                    knownTypes,
                    out _))
            {
                retainedSymbols.Add(target);
            }
        }

        return retainedSymbols;
    }

    private static bool IsCallbackRetainedExpression(
        ExpressionSyntax expression,
        HashSet<string> retainedNames) => expression switch
    {
        IdentifierNameSyntax identifier => retainedNames.Contains(
            identifier.Identifier.ValueText),
        MemberAccessExpressionSyntax memberAccess
            when memberAccess.Name.Identifier.ValueText is "Context" or "Properties" =>
            IsCallbackRetainedExpression(memberAccess.Expression, retainedNames),
        ParenthesizedExpressionSyntax parenthesized =>
            IsCallbackRetainedExpression(parenthesized.Expression, retainedNames),
        CastExpressionSyntax cast => IsCallbackRetainedExpression(cast.Expression, retainedNames),
        PostfixUnaryExpressionSyntax postfix
            when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
            IsCallbackRetainedExpression(postfix.Operand, retainedNames),
        _ => false,
    };

    private static bool TryFindPostAwaitMemberContext(
        InvocationExpressionSyntax invocation,
        HashSet<ISymbol> callbackRetainedSymbols,
        SyntaxNodeAnalysisContext context,
        KnownTypes knownTypes,
        out SyntaxNode capturedContext)
    {
        if (callbackRetainedSymbols.Count == 0)
        {
            capturedContext = null!;
            return false;
        }

        var symbolInfo = context.SemanticModel.GetSymbolInfo(
            invocation,
            context.CancellationToken);
        var methods = symbolInfo.Symbol is IMethodSymbol method
            ? [method]
            : symbolInfo.CandidateSymbols.OfType<IMethodSymbol>();
        foreach (var candidate in methods.Where(static method =>
                     method.DeclaringSyntaxReferences.Length > 0))
        {
            foreach (var syntaxReference in candidate.DeclaringSyntaxReferences)
            {
                var declaration = syntaxReference.GetSyntax(context.CancellationToken);
                if (GetFunctionBody(declaration) is not { } body)
                {
                    continue;
                }

#pragma warning disable RS1030 // Source-backed async calls may be declared in another tree.
                var semanticModel = declaration.SyntaxTree == context.SemanticModel.SyntaxTree
                    ? context.SemanticModel
                    : context.SemanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
#pragma warning restore RS1030
                var nodes = body.DescendantNodesAndSelf(descendIntoChildren: static node =>
                        node is not AnonymousFunctionExpressionSyntax
                            and not LocalFunctionStatementSyntax)
                    .ToArray();
                var awaits = nodes.OfType<AwaitExpressionSyntax>().ToArray();
                if (awaits.Length == 0)
                {
                    continue;
                }

                var controlFlowGraph = TryCreateControlFlowGraph(
                    body,
                    semanticModel,
                    context.CancellationToken);
                var retainedSymbolSeeds = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                foreach (var identifier in nodes.OfType<IdentifierNameSyntax>()
                             .Where(identifier => IsRuntimeValueReference(identifier)))
                {
                    var symbol = semanticModel.GetSymbolInfo(
                        identifier,
                        context.CancellationToken).Symbol;
                    var type = symbol switch
                    {
                        IFieldSymbol field => field.Type,
                        IPropertySymbol property => property.Type,
                        _ => null,
                    };
                    if (symbol is not null
                        && callbackRetainedSymbols.Contains(symbol)
                        && ContainsEventContextReference(type, knownTypes))
                    {
                        retainedSymbolSeeds.Add(symbol);
                    }
                }

                foreach (var awaitExpression in awaits)
                {
                    var retainedNames = new HashSet<string>(StringComparer.Ordinal);
                    var retainedSymbols = new HashSet<ISymbol>(
                        retainedSymbolSeeds,
                        SymbolEqualityComparer.Default);
                    CollectRetainedAliases(
                        nodes,
                        awaitExpression,
                        body,
                        retainedNames,
                        retainedSymbols,
                        semanticModel,
                        controlFlowGraph,
                        cancellationToken: context.CancellationToken);
                    foreach (var identifier in nodes.OfType<IdentifierNameSyntax>()
                                 .Where(identifier => IsRuntimeValueReference(identifier)
                                     && IsRetainedReference(
                                         identifier,
                                         retainedNames,
                                         retainedSymbols,
                                         semanticModel,
                                         context.CancellationToken)
                                     && CanReachAfterSuspension(
                                         awaitExpression,
                                         identifier,
                                         controlFlowGraph)))
                    {
                        capturedContext = identifier;
                        return true;
                    }
                }
            }
        }

        capturedContext = null!;
        return false;
    }

    private static bool TryFindAsyncVoidMethodGroupContext(
        ExpressionSyntax expression,
        SyntaxNodeAnalysisContext context,
        KnownTypes knownTypes,
        out SyntaxNode capturedContext)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(
            expression,
            context.CancellationToken);
        var methods = symbolInfo.Symbol is IMethodSymbol method
            ? [method]
            : symbolInfo.CandidateSymbols.OfType<IMethodSymbol>();

        foreach (var candidate in methods.Where(static method => method.IsAsync && method.ReturnsVoid))
        {
            var eventParameterNames = new HashSet<string>(
                candidate.Parameters
                    .Where(parameter => ContainsEventContextReference(parameter.Type, knownTypes))
                    .Select(static parameter => parameter.Name),
                StringComparer.Ordinal);
            if (eventParameterNames.Count == 0)
            {
                continue;
            }

            foreach (var syntaxReference in candidate.DeclaringSyntaxReferences)
            {
                var declaration = syntaxReference.GetSyntax(context.CancellationToken);
                var body = GetFunctionBody(declaration);
#pragma warning disable RS1030 // Cross-tree method-group CFGs require that tree's semantic model.
                var semanticModel = declaration.SyntaxTree == context.SemanticModel.SyntaxTree
                    ? context.SemanticModel
                    : context.SemanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
#pragma warning restore RS1030
                if (body is not null
                    && TryFindPostAwaitEventContext(
                        body,
                        eventParameterNames,
                        semanticModel,
                        knownTypes,
                        context.CancellationToken,
                        out capturedContext))
                {
                    return true;
                }
            }
        }

        capturedContext = null!;
        return false;
    }

    private static bool TryFindAnonymousFunctionContext(
        AnonymousFunctionExpressionSyntax anonymous,
        SyntaxNodeAnalysisContext context,
        KnownTypes knownTypes,
        out SyntaxNode capturedContext)
    {
        var parameters = (anonymous switch
        {
            SimpleLambdaExpressionSyntax simple => (IEnumerable<ParameterSyntax>)[simple.Parameter],
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters,
            AnonymousMethodExpressionSyntax { ParameterList: { } parameterList } => parameterList.Parameters,
            _ => [],
        }).ToArray();
        var delegateParameters = (context.SemanticModel.GetTypeInfo(
                anonymous,
                context.CancellationToken).ConvertedType as INamedTypeSymbol)
            ?.DelegateInvokeMethod?.Parameters;
        var eventParameterNames = new HashSet<string>(StringComparer.Ordinal);
        var eventParameterSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (delegateParameters is { } symbols)
        {
            for (var index = 0; index < Math.Min(parameters.Length, symbols.Length); index++)
            {
                if (ContainsEventContextReference(symbols[index].Type, knownTypes))
                {
                    eventParameterNames.Add(parameters[index].Identifier.ValueText);
                    if (context.SemanticModel.GetDeclaredSymbol(
                            parameters[index],
                            context.CancellationToken) is { } parameterSymbol)
                    {
                        eventParameterSymbols.Add(parameterSymbol);
                    }
                }
            }
        }

        return TryFindPostAwaitEventContext(
            anonymous.Body,
            eventParameterNames,
            context.SemanticModel,
            knownTypes,
            context.CancellationToken,
            out capturedContext,
            retainedSymbolSeeds: eventParameterSymbols);
    }

    private static bool TryFindPostAwaitEventContext(
        SyntaxNode body,
        HashSet<string> eventParameterNames,
        SemanticModel? semanticModel,
        KnownTypes knownTypes,
        CancellationToken cancellationToken,
        out SyntaxNode capturedContext,
        HashSet<LocalFunctionStatementSyntax>? callPath = null,
        HashSet<ISymbol>? retainedSymbolSeeds = null)
    {
        var nodes = body.DescendantNodesAndSelf(descendIntoChildren: static node =>
                node is not AnonymousFunctionExpressionSyntax
                    and not LocalFunctionStatementSyntax)
            .ToArray();
        var awaits = nodes.OfType<AwaitExpressionSyntax>().ToArray();
        var controlFlowGraph = TryCreateControlFlowGraph(
            body,
            semanticModel,
            cancellationToken);
        var retainedNames = new HashSet<string>(eventParameterNames, StringComparer.Ordinal);
        var retainedSymbols = retainedSymbolSeeds is null
            ? new HashSet<ISymbol>(SymbolEqualityComparer.Default)
            : new HashSet<ISymbol>(retainedSymbolSeeds, SymbolEqualityComparer.Default);
        if (semanticModel is not null)
        {
            foreach (var identifier in nodes.OfType<IdentifierNameSyntax>()
                         .Where(identifier => eventParameterNames.Contains(
                             identifier.Identifier.ValueText)))
            {
                if (semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol
                    is IParameterSymbol parameter)
                {
                    retainedSymbols.Add(parameter);
                }
            }
        }

        var retainedNameSeeds = new HashSet<string>(retainedNames, StringComparer.Ordinal);
        var retainedSymbolSeedsForAwait = new HashSet<ISymbol>(
            retainedSymbols,
            SymbolEqualityComparer.Default);
        var retainedStates = new List<(
            AwaitExpressionSyntax AwaitExpression,
            HashSet<string> Names,
            HashSet<ISymbol> Symbols)>();
        foreach (var awaitExpression in awaits.OrderBy(static candidate => candidate.SpanStart))
        {
            var namesAtAwait = new HashSet<string>(retainedNameSeeds, StringComparer.Ordinal);
            var symbolsAtAwait = new HashSet<ISymbol>(
                retainedSymbolSeedsForAwait,
                SymbolEqualityComparer.Default);
            CollectRetainedAliases(
                nodes,
                awaitExpression,
                body,
                namesAtAwait,
                symbolsAtAwait,
                semanticModel,
                controlFlowGraph,
                cancellationToken);
            retainedStates.Add((awaitExpression, namesAtAwait, symbolsAtAwait));
        }

        foreach (var identifier in nodes.OfType<IdentifierNameSyntax>()
                     .Where(identifier => IsRuntimeValueReference(identifier)
                         && retainedStates.Any(state =>
                             IsRetainedReference(
                                 identifier,
                                 state.Names,
                                 state.Symbols,
                                 semanticModel,
                                 cancellationToken)
                             && CanReachAfterSuspension(
                                 state.AwaitExpression,
                                 identifier,
                                 controlFlowGraph))))
        {
            capturedContext = identifier;
            return true;
        }

        var localFunctions = body.DescendantNodesAndSelf(descendIntoChildren: static node =>
                node is not AnonymousFunctionExpressionSyntax)
            .OfType<LocalFunctionStatementSyntax>()
            .ToLookup(
                static function => function.Identifier.ValueText,
                StringComparer.Ordinal);
        callPath ??= [];
        foreach (var invocation in nodes.OfType<InvocationExpressionSyntax>())
        {
            var reachingStates = retainedStates
                .Where(state => CanReachAfterSuspension(
                    state.AwaitExpression,
                    invocation,
                    controlFlowGraph))
                .ToArray();
            if (reachingStates.Length == 0
                && TryFindRetainedContextInLocalFunction(
                    invocation,
                    localFunctions,
                    retainedNameSeeds,
                    retainedSymbolSeedsForAwait,
                    invokedAfterSuspension: false,
                    semanticModel,
                    knownTypes,
                    cancellationToken,
                    callPath,
                    out capturedContext))
            {
                return true;
            }

            foreach (var state in reachingStates)
            {
                if (TryFindRetainedContextInLocalFunction(
                        invocation,
                        localFunctions,
                        state.Names,
                        state.Symbols,
                        invokedAfterSuspension: true,
                        semanticModel,
                        knownTypes,
                        cancellationToken,
                        callPath,
                        out capturedContext)
                    || TryFindRetainedContextInInvokedDelegate(
                        invocation,
                        state.Names,
                        state.Symbols,
                        semanticModel,
                        cancellationToken,
                        out capturedContext)
                    || TryFindRetainedContextInSourceMethod(
                        invocation,
                        state.Names,
                        state.Symbols,
                        semanticModel,
                        knownTypes,
                        cancellationToken,
                        null,
                        out capturedContext))
                {
                    return true;
                }
            }
        }

        capturedContext = null!;
        return false;
    }

    private static void CollectRetainedAliases(
        SyntaxNode[] nodes,
        SyntaxNode destination,
        SyntaxNode body,
        HashSet<string> retainedNames,
        HashSet<ISymbol> retainedSymbols,
        SemanticModel? semanticModel,
        ControlFlowGraph? controlFlowGraph,
        CancellationToken cancellationToken)
    {
        var retainedNameOrigins = retainedNames.ToDictionary(
            static name => name,
            static _ => new List<SyntaxNode?> { null },
            StringComparer.Ordinal);
        var retainedSymbolOrigins = new Dictionary<ISymbol, List<SyntaxNode?>>(
            SymbolEqualityComparer.Default);
        var retainedNameKills = new Dictionary<string, List<SyntaxNode>>(StringComparer.Ordinal);
        var retainedSymbolKills = new Dictionary<ISymbol, List<SyntaxNode>>(
            SymbolEqualityComparer.Default);
        foreach (var symbol in retainedSymbols)
        {
            retainedSymbolOrigins.Add(symbol, [null]);
        }

        foreach (var alias in nodes
                     .Where(node => node.SpanStart < destination.SpanStart
                         && node is (VariableDeclaratorSyntax or AssignmentExpressionSyntax)
                         && CanReach(node, destination, controlFlowGraph))
                     .OrderBy(static node => node.SpanStart))
        {
            var (target, name, value) = alias switch
            {
                VariableDeclaratorSyntax declarator =>
                    (semanticModel?.GetDeclaredSymbol(declarator, cancellationToken),
                        declarator.Identifier.ValueText,
                        declarator.Initializer?.Value),
                AssignmentExpressionSyntax assignment
                    when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) =>
                    (semanticModel?.GetSymbolInfo(assignment.Left, cancellationToken).Symbol,
                        GetAssignedName(assignment.Left),
                        assignment.Right),
                _ => (null, null, null),
            };
            if (name is null)
            {
                continue;
            }

            if (value is not null
                && HasReachableRetainedAlias(
                    value,
                    alias,
                    retainedNameOrigins,
                    retainedSymbolOrigins,
                    retainedNameKills,
                    retainedSymbolKills,
                    semanticModel,
                    controlFlowGraph,
                    cancellationToken))
            {
                retainedNames.Add(name);
                AddRetainedNameOrigin(retainedNameOrigins, name, alias);
                if (target is not null)
                {
                    retainedSymbols.Add(target);
                    AddRetainedSymbolOrigin(retainedSymbolOrigins, target, alias);
                }
            }
            else
            {
                if (target is not null)
                {
                    AddRetainedSymbolKill(retainedSymbolKills, target, alias);
                }
                else
                {
                    AddRetainedNameKill(retainedNameKills, name, alias);
                }

                if (IsUnconditionalAliasWrite(alias, body))
                {
                    if (target is not null)
                    {
                        retainedSymbols.Remove(target);
                        retainedSymbolOrigins.Remove(target);
                    }
                    else
                    {
                        retainedNames.Remove(name);
                        retainedNameOrigins.Remove(name);
                    }
                }
            }
        }

        retainedNames.RemoveWhere(name =>
        {
            retainedNameKills.TryGetValue(name, out var kills);
            return retainedNameOrigins.TryGetValue(name, out var origins)
                && (!HasReachableOrigin(origins, destination, controlFlowGraph, kills)
                    || IsClearedOnEveryBranch(origins, kills, destination, body));
        });
        retainedSymbols.RemoveWhere(symbol =>
        {
            retainedSymbolKills.TryGetValue(symbol, out var kills);
            return retainedSymbolOrigins.TryGetValue(symbol, out var origins)
                && (!HasReachableOrigin(origins, destination, controlFlowGraph, kills)
                    || IsClearedOnEveryBranch(origins, kills, destination, body));
        });
    }

    private static bool IsClearedOnEveryBranch(
        List<SyntaxNode?> origins,
        List<SyntaxNode>? kills,
        SyntaxNode destination,
        SyntaxNode body)
    {
        if (kills is null || kills.Count < 2)
        {
            return false;
        }

        foreach (var conditional in body.DescendantNodes()
                     .OfType<IfStatementSyntax>()
                     .Where(candidate => candidate.SpanStart < destination.SpanStart
                         && candidate.Else is not null
                         && !origins.Any(origin => origin is not null
                             && origin.SpanStart > candidate.SpanStart
                             && origin.SpanStart < destination.SpanStart)))
        {
            if (IsClearedOnEveryPath(conditional.Statement, kills)
                && IsClearedOnEveryPath(conditional.Else!.Statement, kills))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsClearedOnEveryPath(
        StatementSyntax statement,
        List<SyntaxNode> kills) => statement switch
    {
        ExpressionStatementSyntax expressionStatement =>
            kills.Any(kill => expressionStatement.Span.Contains(kill.Span)),
        BlockSyntax block => block.Statements.Any(child =>
            IsClearedOnEveryPath(child, kills)),
        IfStatementSyntax { Else: { } elseClause } conditional =>
            IsClearedOnEveryPath(conditional.Statement, kills)
            && IsClearedOnEveryPath(elseClause.Statement, kills),
        _ => false,
    };

    private static bool HasReachableRetainedAlias(
        ExpressionSyntax expression,
        SyntaxNode destination,
        Dictionary<string, List<SyntaxNode?>> retainedNameOrigins,
        Dictionary<ISymbol, List<SyntaxNode?>> retainedSymbolOrigins,
        Dictionary<string, List<SyntaxNode>> retainedNameKills,
        Dictionary<ISymbol, List<SyntaxNode>> retainedSymbolKills,
        SemanticModel? semanticModel,
        ControlFlowGraph? controlFlowGraph,
        CancellationToken cancellationToken)
    {
        if (expression is IdentifierNameSyntax identifier)
        {
            var symbol = semanticModel?.GetSymbolInfo(identifier, cancellationToken).Symbol;
            var hasOrigins = symbol is not null
                ? retainedSymbolOrigins.TryGetValue(symbol, out var origins)
                : retainedNameOrigins.TryGetValue(identifier.Identifier.ValueText, out origins);
            List<SyntaxNode>? kills;
            if (symbol is not null)
            {
                retainedSymbolKills.TryGetValue(symbol, out kills);
            }
            else
            {
                retainedNameKills.TryGetValue(identifier.Identifier.ValueText, out kills);
            }

            return hasOrigins
                && HasReachableOrigin(
                    origins!,
                    destination,
                    controlFlowGraph,
                    kills);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var symbol = semanticModel?.GetSymbolInfo(
                memberAccess.Name,
                cancellationToken).Symbol;
            List<SyntaxNode>? kills = null;
            if (symbol is not null)
            {
                retainedSymbolKills.TryGetValue(symbol, out kills);
            }

            if (symbol is not null
                && retainedSymbolOrigins.TryGetValue(symbol, out var origins)
                && HasReachableOrigin(
                    origins,
                    destination,
                    controlFlowGraph,
                    kills))
            {
                return true;
            }

            return memberAccess.Name.Identifier.ValueText is "Context" or "Properties"
                && HasReachableRetainedAlias(
                    memberAccess.Expression,
                    destination,
                    retainedNameOrigins,
                    retainedSymbolOrigins,
                    retainedNameKills,
                    retainedSymbolKills,
                    semanticModel,
                    controlFlowGraph,
                    cancellationToken);
        }

        return expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => HasReachableRetainedAlias(
                parenthesized.Expression,
                destination,
                retainedNameOrigins,
                retainedSymbolOrigins,
                retainedNameKills,
                retainedSymbolKills,
                semanticModel,
                controlFlowGraph,
                cancellationToken),
            CastExpressionSyntax cast => HasReachableRetainedAlias(
                cast.Expression,
                destination,
                retainedNameOrigins,
                retainedSymbolOrigins,
                retainedNameKills,
                retainedSymbolKills,
                semanticModel,
                controlFlowGraph,
                cancellationToken),
            PostfixUnaryExpressionSyntax postfix
                when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
                HasReachableRetainedAlias(
                    postfix.Operand,
                    destination,
                    retainedNameOrigins,
                    retainedSymbolOrigins,
                    retainedNameKills,
                    retainedSymbolKills,
                    semanticModel,
                    controlFlowGraph,
                    cancellationToken),
            _ => false,
        };
    }

    private static bool HasReachableOrigin(
        List<SyntaxNode?> origins,
        SyntaxNode destination,
        ControlFlowGraph? controlFlowGraph,
        List<SyntaxNode>? kills = null) =>
        origins.Any(origin => origin is null
            || CanReachWithoutKills(origin, destination, kills, controlFlowGraph));

    private static bool CanReachWithoutKills(
        SyntaxNode source,
        SyntaxNode target,
        List<SyntaxNode>? kills,
        ControlFlowGraph? controlFlowGraph)
    {
        if (kills is null || kills.Count == 0 || controlFlowGraph is null)
        {
            return CanReach(source, target, controlFlowGraph);
        }

        var sourceSyntax = source is VariableDeclaratorSyntax
            ? source.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>() ?? source
            : source;
        var sourceBlocks = controlFlowGraph.Blocks
            .Where(block => block.IsReachable && ContainsOperationSyntax(block, sourceSyntax))
            .ToArray();
        var targetBlocks = new HashSet<BasicBlock>(controlFlowGraph.Blocks
            .Where(block => block.IsReachable && ContainsOperationSyntax(block, target)));
        if (sourceBlocks.Length == 0 || targetBlocks.Count == 0)
        {
            return CanReach(source, target, controlFlowGraph);
        }

        foreach (var sourceBlock in sourceBlocks)
        {
            if (targetBlocks.Contains(sourceBlock)
                && !ContainsKill(sourceBlock, kills, source.SpanStart, target.SpanStart))
            {
                return true;
            }

            if (ContainsKill(sourceBlock, kills, source.SpanStart, int.MaxValue))
            {
                continue;
            }

            var pending = new Queue<BasicBlock>();
            var visited = new HashSet<BasicBlock>();
            EnqueueSuccessor(sourceBlock.FallThroughSuccessor, pending);
            EnqueueSuccessor(sourceBlock.ConditionalSuccessor, pending);
            while (pending.Count > 0)
            {
                var block = pending.Dequeue();
                if (!visited.Add(block))
                {
                    continue;
                }

                if (targetBlocks.Contains(block))
                {
                    if (!ContainsKill(block, kills, int.MinValue, target.SpanStart))
                    {
                        return true;
                    }

                    continue;
                }

                if (ContainsKill(block, kills, int.MinValue, int.MaxValue))
                {
                    continue;
                }

                EnqueueSuccessor(block.FallThroughSuccessor, pending);
                EnqueueSuccessor(block.ConditionalSuccessor, pending);
            }
        }

        return false;
    }

    private static bool ContainsKill(
        BasicBlock block,
        List<SyntaxNode> kills,
        int after,
        int before) => kills.Any(kill => kill.SpanStart > after
            && kill.SpanStart < before
            && ContainsOperationSyntax(block, kill));

    private static void AddRetainedNameOrigin(
        Dictionary<string, List<SyntaxNode?>> retainedOrigins,
        string name,
        SyntaxNode origin)
    {
        if (!retainedOrigins.TryGetValue(name, out var origins))
        {
            origins = [];
            retainedOrigins.Add(name, origins);
        }

        origins.Add(origin);
    }

    private static void AddRetainedSymbolOrigin(
        Dictionary<ISymbol, List<SyntaxNode?>> retainedOrigins,
        ISymbol symbol,
        SyntaxNode origin)
    {
        if (!retainedOrigins.TryGetValue(symbol, out var origins))
        {
            origins = [];
            retainedOrigins.Add(symbol, origins);
        }

        origins.Add(origin);
    }

    private static void AddRetainedNameKill(
        Dictionary<string, List<SyntaxNode>> retainedKills,
        string name,
        SyntaxNode kill)
    {
        if (!retainedKills.TryGetValue(name, out var kills))
        {
            kills = [];
            retainedKills.Add(name, kills);
        }

        kills.Add(kill);
    }

    private static void AddRetainedSymbolKill(
        Dictionary<ISymbol, List<SyntaxNode>> retainedKills,
        ISymbol symbol,
        SyntaxNode kill)
    {
        if (!retainedKills.TryGetValue(symbol, out var kills))
        {
            kills = [];
            retainedKills.Add(symbol, kills);
        }

        kills.Add(kill);
    }

    private static bool TryFindRetainedContextInLocalFunction(
        InvocationExpressionSyntax invocation,
        ILookup<string, LocalFunctionStatementSyntax> localFunctions,
        HashSet<string> retainedNames,
        HashSet<ISymbol> retainedSymbols,
        bool invokedAfterSuspension,
        SemanticModel? semanticModel,
        KnownTypes knownTypes,
        CancellationToken cancellationToken,
        HashSet<LocalFunctionStatementSyntax> callPath,
        out SyntaxNode capturedContext)
    {
        if (invocation.Expression is not IdentifierNameSyntax identifier)
        {
            capturedContext = null!;
            return false;
        }

        foreach (var function in localFunctions[identifier.Identifier.ValueText])
        {
            if (function.Parent is not BlockSyntax declaringBlock
                || !invocation.Ancestors().Contains(declaringBlock)
                || callPath.Contains(function)
                || GetFunctionBody(function) is not { } functionBody)
            {
                continue;
            }

            var nestedCallPath = new HashSet<LocalFunctionStatementSyntax>(callPath)
            {
                function,
            };
            var functionRetainedNames = new HashSet<string>(retainedNames, StringComparer.Ordinal);
            var functionRetainedSymbols = new HashSet<ISymbol>(
                retainedSymbols,
                SymbolEqualityComparer.Default);
            foreach (var parameter in function.ParameterList.Parameters)
            {
                functionRetainedNames.Remove(parameter.Identifier.ValueText);
            }

            if (!invokedAfterSuspension)
            {
                if (function.Modifiers.Any(SyntaxKind.AsyncKeyword)
                    && TryFindPostAwaitEventContext(
                        functionBody,
                        functionRetainedNames,
                        semanticModel,
                        knownTypes,
                        cancellationToken,
                        out capturedContext,
                        nestedCallPath,
                        functionRetainedSymbols))
                {
                    return true;
                }

                continue;
            }

            var functionNodes = functionBody.DescendantNodesAndSelf(
                    descendIntoChildren: static node =>
                        node is not AnonymousFunctionExpressionSyntax
                            and not LocalFunctionStatementSyntax)
                .ToArray();
            foreach (var retainedIdentifier in functionNodes.OfType<IdentifierNameSyntax>()
                         .Where(candidate => IsRetainedReference(
                                 candidate,
                                 functionRetainedNames,
                                 functionRetainedSymbols,
                                 semanticModel,
                                 cancellationToken)
                             && IsRuntimeValueReference(candidate)))
            {
                capturedContext = retainedIdentifier;
                return true;
            }

            foreach (var nestedInvocation in functionNodes.OfType<InvocationExpressionSyntax>())
            {
                if (TryFindRetainedContextInLocalFunction(
                    nestedInvocation,
                    localFunctions,
                    functionRetainedNames,
                    functionRetainedSymbols,
                    invokedAfterSuspension: true,
                    semanticModel,
                    knownTypes,
                    cancellationToken,
                    nestedCallPath,
                    out capturedContext))
                {
                    return true;
                }
            }
        }

        capturedContext = null!;
        return false;
    }

    private static bool TryFindRetainedContextInSourceMethod(
        InvocationExpressionSyntax invocation,
        HashSet<string> retainedNames,
        HashSet<ISymbol> retainedSymbols,
        SemanticModel? semanticModel,
        KnownTypes knownTypes,
        CancellationToken cancellationToken,
        HashSet<IMethodSymbol>? visitedMethods,
        out SyntaxNode capturedContext)
    {
        if (semanticModel is null
            || semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol
                is not IMethodSymbol { MethodKind: MethodKind.Ordinary } method
            || method.DeclaringSyntaxReferences.Length == 0)
        {
            capturedContext = null!;
            return false;
        }

        visitedMethods ??= new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        if (!visitedMethods.Add(method))
        {
            capturedContext = null!;
            return false;
        }

        try
        {
            var methodRetainedSymbols = new HashSet<ISymbol>(
                retainedSymbols,
                SymbolEqualityComparer.Default);
            if (semanticModel.GetOperation(invocation, cancellationToken)
                    is IInvocationOperation invocationOperation)
            {
                foreach (var argument in invocationOperation.Arguments)
                {
                    if (argument.Parameter is { } parameter
                        && ContainsEventContextReference(argument.Value.Type, knownTypes)
                        && argument.Value.Syntax.DescendantNodesAndSelf()
                            .OfType<IdentifierNameSyntax>()
                            .Any(identifier => IsRetainedReference(
                                identifier,
                                retainedNames,
                                retainedSymbols,
                                semanticModel,
                                cancellationToken)))
                    {
                        methodRetainedSymbols.Add(parameter);
                    }
                }
            }

            foreach (var syntaxReference in method.DeclaringSyntaxReferences)
            {
                var declaration = syntaxReference.GetSyntax(cancellationToken);
                if (GetFunctionBody(declaration) is not { } methodBody)
                {
                    continue;
                }

#pragma warning disable RS1030 // Source-backed helpers may be declared in another tree.
                var methodSemanticModel = declaration.SyntaxTree == semanticModel.SyntaxTree
                    ? semanticModel
                    : semanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
#pragma warning restore RS1030
                var methodRetainedNames = new HashSet<string>(
                    retainedNames,
                    StringComparer.Ordinal);
                foreach (var parameter in method.Parameters)
                {
                    methodRetainedNames.Remove(parameter.Name);
                }

                var methodNodes = methodBody.DescendantNodesAndSelf(
                        descendIntoChildren: static node =>
                            node is not AnonymousFunctionExpressionSyntax
                                and not LocalFunctionStatementSyntax)
                    .ToArray();
                foreach (var retainedIdentifier in methodNodes.OfType<IdentifierNameSyntax>()
                             .Where(candidate => IsRetainedReference(
                                     candidate,
                                     methodRetainedNames,
                                     methodRetainedSymbols,
                                     methodSemanticModel,
                                     cancellationToken)
                                 && IsRuntimeValueReference(candidate)))
                {
                    capturedContext = retainedIdentifier;
                    return true;
                }

                foreach (var nestedInvocation in methodNodes.OfType<InvocationExpressionSyntax>())
                {
                    if (TryFindRetainedContextInSourceMethod(
                        nestedInvocation,
                        methodRetainedNames,
                        methodRetainedSymbols,
                        methodSemanticModel,
                        knownTypes,
                        cancellationToken,
                        visitedMethods,
                        out capturedContext))
                    {
                        return true;
                    }
                }
            }

            capturedContext = null!;
            return false;
        }
        finally
        {
            visitedMethods.Remove(method);
        }
    }

    private static bool TryFindRetainedContextInInvokedDelegate(
        InvocationExpressionSyntax invocation,
        HashSet<string> retainedNames,
        HashSet<ISymbol> retainedSymbols,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken,
        out SyntaxNode capturedContext)
    {
        var identifier = invocation.Expression switch
        {
            IdentifierNameSyntax direct => direct,
            MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax receiver,
                Name.Identifier.ValueText: "Invoke",
            } => receiver,
            _ => null,
        };
        if (semanticModel is null
            || identifier is null
            || semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol
                is not ILocalSymbol local
            || !TryGetStableLocalInitializer(
                local,
                semanticModel,
                cancellationToken,
                identifier,
                out var initializer))
        {
            capturedContext = null!;
            return false;
        }

        foreach (var anonymous in initializer.DescendantNodesAndSelf()
                     .OfType<AnonymousFunctionExpressionSyntax>())
        {
            var capturedNames = new HashSet<string>(retainedNames, StringComparer.Ordinal);
            foreach (var parameter in GetAnonymousFunctionParameters(anonymous))
            {
                capturedNames.Remove(parameter.Identifier.ValueText);
            }

            foreach (var capturedIdentifier in anonymous.Body
                         .DescendantNodesAndSelf(descendIntoChildren: static node =>
                             node is not AnonymousFunctionExpressionSyntax
                                 and not LocalFunctionStatementSyntax)
                         .OfType<IdentifierNameSyntax>()
                         .Where(candidate => IsRetainedReference(
                                 candidate,
                                 capturedNames,
                                 retainedSymbols,
                                 semanticModel,
                                 cancellationToken)
                             && IsRuntimeValueReference(candidate)))
            {
                capturedContext = capturedIdentifier;
                return true;
            }
        }

        capturedContext = null!;
        return false;
    }

    private static IEnumerable<ParameterSyntax> GetAnonymousFunctionParameters(
        AnonymousFunctionExpressionSyntax anonymous) => anonymous switch
    {
        SimpleLambdaExpressionSyntax simple => [simple.Parameter],
        ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters,
        AnonymousMethodExpressionSyntax { ParameterList: { } parameterList } =>
            parameterList.Parameters,
        _ => [],
    };

    private static bool IsRuntimeValueReference(IdentifierNameSyntax identifier) =>
        !identifier.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation =>
            invocation.Expression is IdentifierNameSyntax name
            && name.Identifier.ValueText == "nameof"
            && invocation.ArgumentList.Span.Contains(identifier.Span));

    private static bool IsUnconditionalAliasWrite(SyntaxNode alias, SyntaxNode body)
    {
        for (var current = alias.Parent; current is not null && current != body; current = current.Parent)
        {
            if (current is IfStatementSyntax
                or ElseClauseSyntax
                or SwitchStatementSyntax
                or SwitchExpressionSyntax
                or ConditionalExpressionSyntax
                or ForStatementSyntax
                or ForEachStatementSyntax
                or WhileStatementSyntax
                or DoStatementSyntax
                or TryStatementSyntax
                or CatchClauseSyntax)
            {
                return false;
            }
        }

        return body.Span.Contains(alias.Span);
    }

    private static ControlFlowGraph? TryCreateControlFlowGraph(
        SyntaxNode body,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel is null || semanticModel.SyntaxTree != body.SyntaxTree)
        {
            return null;
        }

        try
        {
            return GetContainingFunction(body) is { } function
                ? TryCreateFunctionControlFlowGraph(
                    function,
                    semanticModel,
                    cancellationToken)
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static ControlFlowGraph? TryCreateFunctionControlFlowGraph(
        SyntaxNode function,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (function is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax)
        {
            return ControlFlowGraph.Create(function, semanticModel, cancellationToken);
        }

        if (GetContainingFunction(function.Parent) is not { } containingFunction
            || TryCreateFunctionControlFlowGraph(
                containingFunction,
                semanticModel,
                cancellationToken) is not { } containingGraph)
        {
            return null;
        }

        if (function is LocalFunctionStatementSyntax localFunction
            && semanticModel.GetDeclaredSymbol(localFunction, cancellationToken)
                is IMethodSymbol symbol)
        {
            return containingGraph.GetLocalFunctionControlFlowGraph(symbol, cancellationToken);
        }

        if (function is AnonymousFunctionExpressionSyntax anonymous)
        {
            foreach (var block in containingGraph.Blocks)
            {
                foreach (var operation in block.Operations
                             .Concat(block.BranchValue is { } branchValue
                                 ? [branchValue]
                                 : []))
                {
                    var flowAnonymous = DescendantOperations(operation)
                        .OfType<IFlowAnonymousFunctionOperation>()
                        .FirstOrDefault(candidate => candidate.Syntax == anonymous);
                    if (flowAnonymous is not null)
                    {
                        return containingGraph.GetAnonymousFunctionControlFlowGraph(
                            flowAnonymous,
                            cancellationToken);
                    }
                }
            }
        }

        return null;
    }

    private static SyntaxNode? GetContainingFunction(SyntaxNode? node) =>
        node?.AncestorsAndSelf().FirstOrDefault(static candidate =>
            candidate is BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AnonymousFunctionExpressionSyntax);

    private static bool CanReachAfterSuspension(
        AwaitExpressionSyntax awaitExpression,
        SyntaxNode candidate,
        ControlFlowGraph? controlFlowGraph)
    {
        if (awaitExpression.SpanStart >= candidate.SpanStart)
        {
            return false;
        }

        if (controlFlowGraph is null)
        {
            return true;
        }

        return CanReach(awaitExpression, candidate, controlFlowGraph);
    }

    private static bool CanReach(
        SyntaxNode source,
        SyntaxNode target,
        ControlFlowGraph? controlFlowGraph)
    {
        if (controlFlowGraph is null)
        {
            return true;
        }

        var sourceSyntax = source is VariableDeclaratorSyntax
            ? source.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>() ?? source
            : source;
        var sourceBlocks = controlFlowGraph.Blocks
            .Where(block => block.IsReachable
                && ContainsOperationSyntax(block, sourceSyntax))
            .ToArray();
        var targetBlocks = new HashSet<BasicBlock>(controlFlowGraph.Blocks
            .Where(block => block.IsReachable && ContainsOperationSyntax(block, target)));
        if (sourceBlocks.Length == 0 || targetBlocks.Count == 0)
        {
            return true;
        }

        foreach (var sourceBlock in sourceBlocks)
        {
            if (targetBlocks.Contains(sourceBlock))
            {
                return true;
            }

            var pending = new Queue<BasicBlock>();
            var visited = new HashSet<BasicBlock>();
            EnqueueSuccessor(sourceBlock.FallThroughSuccessor, pending);
            EnqueueSuccessor(sourceBlock.ConditionalSuccessor, pending);
            while (pending.Count > 0)
            {
                var block = pending.Dequeue();
                if (!visited.Add(block))
                {
                    continue;
                }

                if (targetBlocks.Contains(block))
                {
                    return true;
                }

                EnqueueSuccessor(block.FallThroughSuccessor, pending);
                EnqueueSuccessor(block.ConditionalSuccessor, pending);
            }
        }

        return false;
    }

    private static void EnqueueSuccessor(
        ControlFlowBranch? branch,
        Queue<BasicBlock> pending)
    {
        if (branch?.Destination is { IsReachable: true } destination)
        {
            pending.Enqueue(destination);
        }
    }

    private static bool ContainsOperationSyntax(BasicBlock block, SyntaxNode syntax) =>
        block.Operations.Any(operation => DescendantOperations(operation)
            .Any(candidate => candidate.Syntax == syntax))
        || block.BranchValue is { } branchValue
            && DescendantOperations(branchValue).Any(candidate => candidate.Syntax == syntax);

    private static string? GetAssignedName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
        _ => null,
    };

    private static bool IsRetainedAliasExpression(
        ExpressionSyntax expression,
        HashSet<string> retainedNames,
        HashSet<ISymbol> retainedSymbols,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken) => expression switch
    {
        IdentifierNameSyntax identifier => IsRetainedReference(
            identifier,
            retainedNames,
            retainedSymbols,
            semanticModel,
            cancellationToken),
        MemberAccessExpressionSyntax { Name: IdentifierNameSyntax identifier }
            when semanticModel is not null
                && IsRetainedReference(
                identifier,
                retainedNames,
                retainedSymbols,
                semanticModel,
                cancellationToken) => true,
        MemberAccessExpressionSyntax memberAccess
            when memberAccess.Name.Identifier.ValueText is "Context" or "Properties" =>
            IsRetainedAliasExpression(
                memberAccess.Expression,
                retainedNames,
                retainedSymbols,
                semanticModel,
                cancellationToken),
        ParenthesizedExpressionSyntax parenthesized =>
            IsRetainedAliasExpression(
                parenthesized.Expression,
                retainedNames,
                retainedSymbols,
                semanticModel,
                cancellationToken),
        CastExpressionSyntax cast => IsRetainedAliasExpression(
            cast.Expression,
            retainedNames,
            retainedSymbols,
            semanticModel,
            cancellationToken),
        PostfixUnaryExpressionSyntax postfix
            when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
            IsRetainedAliasExpression(
                postfix.Operand,
                retainedNames,
                retainedSymbols,
                semanticModel,
                cancellationToken),
        _ => false,
    };

    private static bool IsRetainedReference(
        IdentifierNameSyntax identifier,
        HashSet<string> retainedNames,
        HashSet<ISymbol> retainedSymbols,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken) =>
        semanticModel?.GetSymbolInfo(identifier, cancellationToken).Symbol is { } symbol
            ? retainedSymbols.Contains(symbol)
            : retainedNames.Contains(identifier.Identifier.ValueText);

    private static SyntaxNode? GetFunctionBody(SyntaxNode declaration) => declaration switch
    {
        MethodDeclarationSyntax { Body: { } block } => block,
        MethodDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
        LocalFunctionStatementSyntax { Body: { } block } => block,
        LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression } => expression,
        _ => null,
    };

    private static bool TryFindEventContextExpression(
        ExpressionSyntax expression,
        SyntaxNodeAnalysisContext context,
        KnownTypes knownTypes,
        out SyntaxNode capturedContext)
    {
        var operation = Unwrap(context.SemanticModel.GetOperation(
            expression,
            context.CancellationToken));
        if (operation is not null)
        {
            foreach (var candidate in RetainedValueOperations(
                operation,
                context.SemanticModel,
                context.CancellationToken))
            {
                var unwrapped = Unwrap(candidate)!;
                if (ContainsEventContextReference(unwrapped.Type, knownTypes))
                {
                    capturedContext = unwrapped.Syntax;
                    return true;
                }
            }
        }
        else if (ContainsEventContextReference(
            context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type,
            knownTypes))
        {
            capturedContext = expression;
            return true;
        }

        capturedContext = null!;
        return false;
    }

    private static IEnumerable<IOperation> RetainedValueOperations(
        IOperation root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<IOperation>();
        var visitedLocals = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = Unwrap(stack.Pop())!;
            yield return current;

            if (current is ILocalReferenceOperation localReference
                && visitedLocals.Add(localReference.Local)
                && TryGetStableLocalInitializer(
                    localReference.Local,
                    semanticModel,
                    cancellationToken,
                    localReference.Syntax,
                    out var initializer)
                && semanticModel.GetOperation(initializer, cancellationToken) is { } initializerOperation)
            {
                stack.Push(initializerOperation);
            }

            if (current is IAnonymousFunctionOperation anonymous)
            {
                foreach (var descendant in DescendantOperations(anonymous).Skip(1))
                {
                    if (ContainsAnonymousOwnedReference(descendant, anonymous.Symbol))
                    {
                        continue;
                    }

                    stack.Push(descendant);
                }

                continue;
            }

            if (current is IDelegateCreationOperation delegateCreation)
            {
                stack.Push(delegateCreation.Target);
                continue;
            }

            foreach (var child in GetRetainedValueParts(current))
            {
                stack.Push(child);
            }
        }
    }

    private static bool ContainsAnonymousOwnedReference(
        IOperation operation,
        IMethodSymbol anonymousSymbol) => DescendantOperations(operation).Any(candidate =>
            (candidate is IParameterReferenceOperation parameterReference
                && SymbolEqualityComparer.Default.Equals(
                    parameterReference.Parameter.ContainingSymbol,
                    anonymousSymbol))
            || (candidate is ILocalReferenceOperation localReference
                && SymbolEqualityComparer.Default.Equals(
                    localReference.Local.ContainingSymbol,
                    anonymousSymbol)));

    private static IEnumerable<IOperation> GetRetainedValueParts(IOperation operation)
    {
        switch (operation)
        {
            case IConditionalOperation conditional:
                yield return conditional.WhenTrue;
                if (conditional.WhenFalse is { } whenFalse)
                {
                    yield return whenFalse;
                }

                break;
            case ICoalesceOperation coalesce:
                yield return coalesce.Value;
                yield return coalesce.WhenNull;
                break;
            case ISwitchExpressionOperation switchExpression:
                foreach (var arm in switchExpression.Arms)
                {
                    yield return arm.Value;
                }

                break;
            case IArrayCreationOperation { Initializer: { } arrayInitializer }:
                foreach (var element in arrayInitializer.ElementValues)
                {
                    yield return element;
                }

                break;
            case ITupleOperation tuple:
                foreach (var element in tuple.Elements)
                {
                    yield return element;
                }

                break;
            case IObjectCreationOperation objectCreation:
                foreach (var argument in objectCreation.Arguments)
                {
                    yield return argument.Value;
                }

                if (objectCreation.Initializer is { } objectInitializer)
                {
                    foreach (var initializer in objectInitializer.Initializers)
                    {
                        if (initializer is ISimpleAssignmentOperation assignment)
                        {
                            yield return assignment.Value;
                        }
                        else if (initializer is IInvocationOperation invocation)
                        {
                            foreach (var argument in invocation.Arguments)
                            {
                                yield return argument.Value;
                            }
                        }
                    }
                }

                break;
            case IWithOperation withOperation:
                yield return withOperation.Operand;
                foreach (var initializer in withOperation.Initializer.Initializers)
                {
                    if (initializer is ISimpleAssignmentOperation assignment)
                    {
                        yield return assignment.Value;
                    }
                }

                break;
            case IAnonymousObjectCreationOperation anonymousObjectCreation:
                foreach (var initializer in anonymousObjectCreation.Initializers)
                {
                    yield return initializer is ISimpleAssignmentOperation assignment
                        ? assignment.Value
                        : initializer;
                }

                break;
            default:
                if (operation.Syntax is CollectionExpressionSyntax or SpreadElementSyntax)
                {
                    foreach (var child in operation.ChildOperations)
                    {
                        yield return child;
                    }
                }

                break;
        }
    }

    private static IEnumerable<InvocationExpressionSyntax> GetCallbackInvocations(
        SyntaxNode body,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool followTaskReturningLocalFunctions)
    {
        var pendingBodies = new Stack<SyntaxNode>();
        var visitedLocalFunctions = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        pendingBodies.Push(body);
        while (pendingBodies.Count > 0)
        {
            foreach (var invocation in pendingBodies.Pop()
                         .DescendantNodesAndSelf(descendIntoChildren: static node =>
                             node is not AnonymousFunctionExpressionSyntax
                                 and not LocalFunctionStatementSyntax)
                         .OfType<InvocationExpressionSyntax>())
            {
                yield return invocation;
                if (semanticModel.GetSymbolInfo(
                        invocation,
                        cancellationToken).Symbol is not IMethodSymbol
                    {
                        MethodKind: MethodKind.LocalFunction,
                    } localFunction
                    || !localFunction.ReturnsVoid
                        && (!followTaskReturningLocalFunctions
                            || !StartsAsynchronousWork(localFunction))
                    || !visitedLocalFunctions.Add(localFunction))
                {
                    continue;
                }

                foreach (var syntaxReference in localFunction.DeclaringSyntaxReferences)
                {
                    var declaration = syntaxReference.GetSyntax(cancellationToken);
                    var localBody = GetFunctionBody(declaration);
                    if (localBody is not null)
                    {
                        pendingBodies.Push(localBody);
                    }
                }
            }
        }
    }

    private static bool IsUnobservedAsyncInvocation(
        InvocationExpressionSyntax invocation,
        SyntaxNodeAnalysisContext context)
    {
        if (TryGetInvokedStableDelegateInitializer(
                invocation,
                context.SemanticModel,
                context.CancellationToken,
                out var initializer)
            && StartsAsynchronousWork(initializer, context)
            && !IsSynchronouslyObserved(
                invocation,
                context.SemanticModel,
                context.CancellationToken))
        {
            return true;
        }

        return context.SemanticModel.GetSymbolInfo(
                invocation,
                context.CancellationToken).Symbol is IMethodSymbol method
            && !IsDeferredScheduler(method)
            && StartsAsynchronousWork(method)
            && !IsSynchronouslyObserved(
                invocation,
                context.SemanticModel,
                context.CancellationToken);
    }

    private static bool TryGetInvokedStableDelegateInitializer(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax initializer)
    {
        var identifier = invocation.Expression switch
        {
            IdentifierNameSyntax direct => direct,
            MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax receiver,
                Name.Identifier.ValueText: "Invoke",
            } => receiver,
            _ => null,
        };
        if (identifier is not null
            && semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol
                is ILocalSymbol { Type.TypeKind: TypeKind.Delegate } local
            && TryGetStableLocalInitializer(
                local,
                semanticModel,
                cancellationToken,
                identifier,
                out initializer))
        {
            return true;
        }

        var expression = invocation.Expression;
        while (true)
        {
            if (expression is AnonymousFunctionExpressionSyntax anonymous)
            {
                initializer = anonymous;
                return true;
            }

            var parts = GetCallbackExpressionParts(
                    expression,
                    semanticModel,
                    cancellationToken)
                .ToArray();
            if (parts.Length != 1)
            {
                break;
            }

            expression = parts[0];
        }

        initializer = null!;
        return false;
    }

    private static bool IsSynchronouslyObserved(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        IsSynchronouslyObservedCore(invocation, semanticModel, cancellationToken)
        || IsSynchronouslyObservedThroughLocal(
            invocation,
            semanticModel,
            cancellationToken);

    private static bool IsSynchronouslyObservedCore(
        SyntaxNode value,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var current = value;
        while (current.Parent is MemberAccessExpressionSyntax memberAccess
               && memberAccess.Expression == current)
        {
            if (memberAccess.Parent is not InvocationExpressionSyntax consumer
                || semanticModel.GetSymbolInfo(
                    consumer,
                    cancellationToken).Symbol is not IMethodSymbol method)
            {
                return semanticModel.GetSymbolInfo(
                        memberAccess,
                        cancellationToken).Symbol is IPropertySymbol
                    {
                        Name: "Result",
                        ContainingType: { } resultContainingType,
                    }
                    && IsTaskLike(resultContainingType);
            }

            if ((IsFrameworkAwaiterGetResult(method)
                    && consumer.ArgumentList.Arguments.Count == 0)
                || (method.Name == "Wait"
                    && consumer.ArgumentList.Arguments.Count == 0
                    && method.ContainingType is { } waitContainingType
                    && IsTaskLike(waitContainingType)))
            {
                return true;
            }

            if (method.Name is not ("ConfigureAwait" or "GetAwaiter")
                && (method.Name != "AsTask" || !IsTaskLike(method.ContainingType)))
            {
                break;
            }

            current = consumer;
        }

        if (current.Parent is AwaitExpressionSyntax
            && IsTaskReturningFunction(current, semanticModel, cancellationToken))
        {
            return true;
        }

        if (IsReturnedTaskWrapper(current, semanticModel, cancellationToken))
        {
            return true;
        }

        if (current.Ancestors()
                .TakeWhile(static node => node is not AnonymousFunctionExpressionSyntax
                    and not StatementSyntax)
                .OfType<ArgumentSyntax>()
                .FirstOrDefault() is not { } argument
            || !IsObservationPreservingArgumentPath(current, argument)
            || argument.Parent?.Parent is not InvocationExpressionSyntax consumerInvocation
            || semanticModel.GetSymbolInfo(
                consumerInvocation,
                cancellationToken).Symbol is not IMethodSymbol consumerMethod
            || !IsTaskLike(consumerMethod.ContainingType))
        {
            return false;
        }

        if (consumerMethod.Name == "WaitAll"
            && consumerMethod.Parameters.Length == 1
            && consumerMethod.Parameters[0].Type is IArrayTypeSymbol
            {
                ElementType: { } elementType,
            }
            && IsTaskLike(elementType))
        {
            return true;
        }

        return consumerMethod.Name == "WhenAll"
            && IsSynchronouslyObserved(
                consumerInvocation,
                semanticModel,
                cancellationToken);
    }

    private static bool IsTaskReturningFunction(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        GetContainingFunction(node) switch
        {
            AnonymousFunctionExpressionSyntax anonymous =>
                semanticModel.GetTypeInfo(anonymous, cancellationToken).ConvertedType
                    is INamedTypeSymbol { DelegateInvokeMethod.ReturnType: { } returnType }
                && IsTaskLike(returnType),
            LocalFunctionStatementSyntax localFunction =>
                semanticModel.GetDeclaredSymbol(localFunction, cancellationToken)
                    is IMethodSymbol { ReturnType: { } returnType }
                && IsTaskLike(returnType),
            BaseMethodDeclarationSyntax method =>
                semanticModel.GetDeclaredSymbol(method, cancellationToken)
                    is IMethodSymbol { ReturnType: { } returnType }
                && IsTaskLike(returnType),
            _ => false,
        };

    private static bool IsReturnedTaskWrapper(
        SyntaxNode value,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var current = value;
        while (current.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
        {
            current = current.Parent;
        }

        if (current.Parent is not ArgumentSyntax
            {
                Parent.Parent: BaseObjectCreationExpressionSyntax creation,
            }
            || !IsTaskLike(semanticModel.GetTypeInfo(creation, cancellationToken).Type))
        {
            return false;
        }

        current = creation;
        while (current.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
        {
            current = current.Parent;
        }

        return IsTaskReturningFunction(current, semanticModel, cancellationToken)
            && (current.Parent is ReturnStatementSyntax
                || current.Parent is ArrowExpressionClauseSyntax
                || current.Parent is LambdaExpressionSyntax lambda
                    && lambda.ExpressionBody == current);
    }

    private static bool IsSynchronouslyObservedThroughLocal(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Parent is not EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax declarator,
            }
            || semanticModel.GetDeclaredSymbol(
                declarator,
                cancellationToken) is not ILocalSymbol local
            || declarator.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>()
                is not { Parent: BlockSyntax block } declarationStatement)
        {
            return false;
        }

        foreach (var reference in block.DescendantNodes(descendIntoChildren: static node =>
                     node is not AnonymousFunctionExpressionSyntax
                         and not LocalFunctionStatementSyntax)
                 .OfType<IdentifierNameSyntax>()
                 .Where(reference => reference.SpanStart > declarator.SpanStart))
        {
            if (reference.FirstAncestorOrSelf<StatementSyntax>()
                    is not { Parent: BlockSyntax observationBlock } observationStatement
                || observationBlock != block
                || block.Statements.Any(statement =>
                    statement.SpanStart > declarationStatement.SpanStart
                        && statement.SpanStart < observationStatement.SpanStart
                        && IsPotentiallyThrowingOrBranchingStatement(
                            statement,
                            semanticModel,
                            cancellationToken))
                || !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(reference, cancellationToken).Symbol,
                    local)
                || !TryGetStableLocalInitializer(
                    local,
                    semanticModel,
                    cancellationToken,
                    reference,
                    out var initializer)
                || !initializer.Span.Contains(invocation.Span)
                || !IsSynchronouslyObservedCore(
                    reference,
                    semanticModel,
                    cancellationToken)
                || !IsGuaranteedBeforeFunctionExit(
                    invocation,
                    reference,
                    semanticModel,
                    cancellationToken))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsPotentiallyThrowingOrBranchingStatement(
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        statement switch
        {
            EmptyStatementSyntax => false,
            LocalDeclarationStatementSyntax declaration => declaration.Declaration.Variables
                .Any(variable => variable.Initializer is { Value: { } value }
                    && !semanticModel.GetConstantValue(value, cancellationToken).HasValue
                    && value is not (DefaultExpressionSyntax or LiteralExpressionSyntax
                    {
                        RawKind: (int)SyntaxKind.DefaultLiteralExpression,
                    })),
            _ => true,
        };

    private static bool IsGuaranteedBeforeFunctionExit(
        SyntaxNode start,
        SyntaxNode observation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var controlFlowGraph = TryCreateControlFlowGraph(
            start,
            semanticModel,
            cancellationToken);
        if (controlFlowGraph is null)
        {
            return false;
        }

        var observationBlocks = new HashSet<BasicBlock>(controlFlowGraph.Blocks
            .Where(block => block.IsReachable && ContainsOperationSyntax(block, observation)));
        if (observationBlocks.Count == 0)
        {
            return false;
        }

        var startBlocks = controlFlowGraph.Blocks
            .Where(block => block.IsReachable && ContainsOperationSyntax(block, start))
            .ToArray();
        if (startBlocks.Length == 0)
        {
            return false;
        }

        foreach (var startBlock in startBlocks)
        {
            if (observationBlocks.Contains(startBlock))
            {
                continue;
            }

            var pending = new Queue<BasicBlock>();
            var visited = new HashSet<BasicBlock>();
            EnqueueSuccessor(startBlock.FallThroughSuccessor, pending);
            EnqueueSuccessor(startBlock.ConditionalSuccessor, pending);
            while (pending.Count > 0)
            {
                var block = pending.Dequeue();
                if (!visited.Add(block) || observationBlocks.Contains(block))
                {
                    continue;
                }

                if (block.Kind == BasicBlockKind.Exit)
                {
                    return false;
                }

                EnqueueSuccessor(block.FallThroughSuccessor, pending);
                EnqueueSuccessor(block.ConditionalSuccessor, pending);
            }
        }

        return true;
    }

    private static bool IsObservationPreservingArgumentPath(
        SyntaxNode value,
        ArgumentSyntax argument)
    {
        for (var current = value.Parent; current != argument; current = current?.Parent)
        {
            if (current is null
                || current is not (InitializerExpressionSyntax
                    or ArrayCreationExpressionSyntax
                    or ImplicitArrayCreationExpressionSyntax
                    or CollectionExpressionSyntax
                    or ParenthesizedExpressionSyntax
                    or CastExpressionSyntax
                    or PostfixUnaryExpressionSyntax))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFrameworkAwaiterGetResult(IMethodSymbol method)
    {
        if (method.Name != "GetResult")
        {
            return false;
        }

        var definition = method.ContainingType.OriginalDefinition.ToDisplayString();
        return definition is "System.Runtime.CompilerServices.TaskAwaiter"
            or "System.Runtime.CompilerServices.TaskAwaiter<TResult>"
            or "System.Runtime.CompilerServices.ValueTaskAwaiter<TResult>"
            or "System.Runtime.CompilerServices.ValueTaskAwaiter"
            or "System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter"
            or "System.Runtime.CompilerServices.ConfiguredTaskAwaitable<TResult>.ConfiguredTaskAwaiter"
            or "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable.ConfiguredValueTaskAwaiter"
            or "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<TResult>.ConfiguredValueTaskAwaiter";
    }

    private static bool IsTaskLike(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var definition = named.OriginalDefinition.ToDisplayString();
        return definition is "System.Threading.Tasks.Task"
            or "System.Threading.Tasks.Task<TResult>"
            or "System.Threading.Tasks.ValueTask"
            or "System.Threading.Tasks.ValueTask<TResult>";
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, KnownTypes knownTypes)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (IsDeferredScheduler(invocation.TargetMethod)
            && (invocation.Syntax is not InvocationExpressionSyntax invocationSyntax
                || context.Operation.SemanticModel is not { } semanticModel
                || !IsSynchronouslyObserved(
                    invocationSyntax,
                    semanticModel,
                    context.CancellationToken))
            && TryFindCapturedEventContext(
                invocation,
                context,
                knownTypes,
                out var capturedContext))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DeferredContextCaptureRule,
                capturedContext.GetLocation()));
        }

        if (IsSynchronousExecute(invocation.TargetMethod, knownTypes))
        {
            string? asyncMember = null;
            if (FindInPipeline(
                    GetReceiver(invocation),
                    context,
                    candidate => TryFindAsyncConfiguration(candidate, context, knownTypes, out asyncMember),
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

    private static bool IsDeferredScheduler(IMethodSymbol method) =>
        method.Name == "Run"
            && method.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task"
        || method.Name is "QueueUserWorkItem" or "UnsafeQueueUserWorkItem"
            && method.ContainingType.ToDisplayString() == "System.Threading.ThreadPool";

    private static bool TryFindCapturedEventContext(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        out SyntaxNode capturedContext)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type.TypeKind == TypeKind.Delegate)
            {
                if (TryFindCapturedEventContext(
                        argument.Value,
                        context,
                        knownTypes,
                        visitedLocals: null,
                        out capturedContext))
                {
                    return true;
                }
            }
            else if (TryFindDeferredStateContext(
                argument.Value,
                context,
                knownTypes,
                visitedLocals: null,
                out capturedContext))
            {
                return true;
            }
        }

        capturedContext = null!;
        return false;
    }

    private static bool TryFindDeferredStateContext(
        IOperation operation,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        HashSet<ISymbol>? visitedLocals,
        out SyntaxNode capturedContext)
    {
        operation = Unwrap(operation)!;
        if (operation is IDefaultValueOperation
            || operation.ConstantValue is { HasValue: true, Value: null })
        {
            capturedContext = null!;
            return false;
        }

        if (operation is IConditionalOperation conditional
            && (TryFindDeferredStateContext(
                    conditional.WhenTrue,
                    context,
                    knownTypes,
                    visitedLocals,
                    out capturedContext)
                || conditional.WhenFalse is { } whenFalse
                    && TryFindDeferredStateContext(
                        whenFalse,
                        context,
                        knownTypes,
                        visitedLocals,
                        out capturedContext)))
        {
            return true;
        }

        if (operation is ICoalesceOperation coalesce
            && (TryFindDeferredStateContext(
                    coalesce.Value,
                    context,
                    knownTypes,
                    visitedLocals,
                    out capturedContext)
                || TryFindDeferredStateContext(
                    coalesce.WhenNull,
                    context,
                    knownTypes,
                    visitedLocals,
                    out capturedContext)))
        {
            return true;
        }

        if (operation is ISwitchExpressionOperation switchExpression)
        {
            foreach (var arm in switchExpression.Arms)
            {
                if (TryFindDeferredStateContext(
                    arm.Value,
                    context,
                    knownTypes,
                    visitedLocals,
                    out capturedContext))
                {
                    return true;
                }
            }
        }

        if (operation is ILocalReferenceOperation localReference)
        {
            visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (visitedLocals.Add(localReference.Local)
                && TryGetStableInitializer(localReference, context, out var initializer)
                && initializer is not null)
            {
                if (TryFindDeferredStateContext(
                    initializer,
                    context,
                    knownTypes,
                    visitedLocals,
                    out capturedContext))
                {
                    return true;
                }

                visitedLocals.Remove(localReference.Local);
                capturedContext = null!;
                return false;
            }

            visitedLocals.Remove(localReference.Local);
        }

        if (operation is IArrayCreationOperation { Initializer: { } arrayInitializer })
        {
            foreach (var element in arrayInitializer.ElementValues)
            {
                if (TryFindDeferredStateContext(
                    element,
                    context,
                    knownTypes,
                    visitedLocals,
                    out capturedContext))
                {
                    return true;
                }
            }
        }

        if (operation is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements)
            {
                if (TryFindDeferredStateContext(
                    element,
                    context,
                    knownTypes,
                    visitedLocals,
                    out capturedContext))
                {
                    return true;
                }
            }
        }

        if (operation.Syntax is CollectionExpressionSyntax or SpreadElementSyntax)
        {
            foreach (var child in operation.ChildOperations)
            {
                if (TryFindDeferredStateContext(
                    child,
                    context,
                    knownTypes,
                    visitedLocals,
                    out capturedContext))
                {
                    return true;
                }
            }
        }

        if (operation is IObjectCreationOperation objectCreation)
        {
            foreach (var argument in objectCreation.Arguments)
            {
                if (TryFindDeferredStateContext(
                    argument.Value,
                    context,
                    knownTypes,
                    visitedLocals,
                    out capturedContext))
                {
                    return true;
                }
            }

            if (objectCreation.Initializer is { } objectInitializer)
            {
                foreach (var initializer in objectInitializer.Initializers)
                {
                    if (initializer is ISimpleAssignmentOperation assignment
                        && TryFindDeferredStateContext(
                            assignment.Value,
                            context,
                            knownTypes,
                            visitedLocals,
                            out capturedContext))
                    {
                        return true;
                    }

                    if (initializer is IInvocationOperation invocation)
                    {
                        foreach (var argument in invocation.Arguments)
                        {
                            if (TryFindDeferredStateContext(
                                argument.Value,
                                context,
                                knownTypes,
                                visitedLocals,
                                out capturedContext))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }

        if (operation is IWithOperation withOperation)
        {
            if (TryFindDeferredStateContext(
                withOperation.Operand,
                context,
                knownTypes,
                visitedLocals,
                out capturedContext))
            {
                return true;
            }

            foreach (var initializer in withOperation.Initializer.Initializers)
            {
                if (initializer is ISimpleAssignmentOperation assignment
                    && TryFindDeferredStateContext(
                        assignment.Value,
                        context,
                        knownTypes,
                        visitedLocals,
                        out capturedContext))
                {
                    return true;
                }
            }
        }

        if (operation is IAnonymousObjectCreationOperation anonymousObjectCreation)
        {
            foreach (var initializer in anonymousObjectCreation.Initializers)
            {
                var value = initializer is ISimpleAssignmentOperation assignment
                    ? assignment.Value
                    : initializer;
                if (TryFindDeferredStateContext(
                    value,
                    context,
                    knownTypes,
                    visitedLocals,
                    out capturedContext))
                {
                    return true;
                }
            }
        }

        if (IsKnownCompositeState(operation))
        {
            capturedContext = null!;
            return false;
        }

        if (ContainsEventContextReference(operation.Type, knownTypes))
        {
            capturedContext = operation.Syntax;
            return true;
        }

        capturedContext = null!;
        return false;
    }

    private static bool IsKnownCompositeState(IOperation operation) =>
        operation is IConditionalOperation
            or ICoalesceOperation
            or ISwitchExpressionOperation
            or IArrayCreationOperation
            or ITupleOperation
            or IObjectCreationOperation
            or IWithOperation
            or IAnonymousObjectCreationOperation
        || operation.Syntax is CollectionExpressionSyntax or SpreadElementSyntax
        || operation is IInvocationOperation
        {
            TargetMethod:
            {
                Name: "Empty",
                Parameters.Length: 0,
                ContainingType: { } containingType,
            },
        }
            && containingType.ToDisplayString() is "System.Array" or "System.Linq.Enumerable";

    private static bool ContainsEventContextReference(ITypeSymbol? type, KnownTypes knownTypes) =>
        knownTypes.IsEventContextReference(type)
        || knownTypes.IsEventContextContainer(type)
        || type is IArrayTypeSymbol array
            && ContainsEventContextReference(array.ElementType, knownTypes)
        || type is INamedTypeSymbol named
            && named.TypeArguments.Any(argument =>
                ContainsEventContextReference(argument, knownTypes));

    private static bool TryFindCapturedEventContext(
        IOperation root,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        HashSet<ISymbol>? visitedLocals,
        out SyntaxNode capturedContext)
    {
        root = Unwrap(root)!;
        switch (root)
        {
            case IDelegateCreationOperation delegateCreation:
                return TryFindCapturedEventContext(
                    delegateCreation.Target,
                    context,
                    knownTypes,
                    visitedLocals,
                    out capturedContext);
            case IAnonymousFunctionOperation:
                return TryFindCapturedEventContextInDelegate(
                    root,
                    context,
                    knownTypes,
                    visitedLocals,
                    provenanceAnchor: null,
                    out capturedContext);
            case IMethodReferenceOperation methodReference:
                if (methodReference.Method.MethodKind is MethodKind.LocalFunction or MethodKind.Ordinary
                    && methodReference.Method.DeclaringSyntaxReferences.Length > 0)
                {
                    visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                    if (TryFindCapturedEventContextInMethod(
                        methodReference.Method,
                        context,
                        knownTypes,
                        visitedLocals,
                        methodReference.Syntax,
                        out capturedContext))
                    {
                        return true;
                    }
                }

                if (methodReference.Instance is { } receiver)
                {
                    return TryFindDeferredStateContext(
                        receiver,
                        context,
                        knownTypes,
                        visitedLocals,
                        out capturedContext);
                }

                break;
            case ILocalReferenceOperation localReference
                when localReference.Local.Type.TypeKind == TypeKind.Delegate:
                visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                if (visitedLocals.Add(localReference.Local)
                    && TryGetStableInitializer(localReference, context, out var initializer)
                    && initializer is not null
                    && TryFindCapturedEventContext(
                        initializer,
                        context,
                        knownTypes,
                        visitedLocals,
                        out _))
                {
                    capturedContext = localReference.Syntax;
                    return true;
                }

                visitedLocals.Remove(localReference.Local);
                break;
            case IConditionalOperation conditional:
                if (TryFindCapturedEventContext(
                        conditional.WhenTrue,
                        context,
                        knownTypes,
                        visitedLocals,
                        out capturedContext)
                    || conditional.WhenFalse is { } whenFalse
                        && TryFindCapturedEventContext(
                            whenFalse,
                            context,
                            knownTypes,
                            visitedLocals,
                            out capturedContext))
                {
                    return true;
                }

                break;
            case ICoalesceOperation coalesce:
                if (TryFindCapturedEventContext(
                        coalesce.Value,
                        context,
                        knownTypes,
                        visitedLocals,
                        out capturedContext)
                    || coalesce.WhenNull is { } whenNull
                        && TryFindCapturedEventContext(
                            whenNull,
                            context,
                            knownTypes,
                            visitedLocals,
                            out capturedContext))
                {
                    return true;
                }

                break;
            case ISwitchExpressionOperation switchExpression:
                foreach (var arm in switchExpression.Arms)
                {
                    if (TryFindCapturedEventContext(
                        arm.Value,
                        context,
                        knownTypes,
                        visitedLocals,
                        out capturedContext))
                    {
                        return true;
                    }
                }

                break;
        }

        capturedContext = null!;
        return false;
    }

    private static bool TryFindCapturedEventContextInMethod(
        IMethodSymbol method,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        HashSet<ISymbol> visitedSymbols,
        SyntaxNode provenanceAnchor,
        out SyntaxNode capturedContext)
    {
        if (!visitedSymbols.Add(method))
        {
            capturedContext = null!;
            return false;
        }

        try
        {
            var currentSemanticModel = context.Operation.SemanticModel;
            if (currentSemanticModel is not null)
            {
                foreach (var syntaxReference in method.DeclaringSyntaxReferences)
                {
                    var syntax = syntaxReference.GetSyntax(context.CancellationToken);
#pragma warning disable RS1030 // Source-backed method groups may be declared in another tree.
                    var semanticModel = syntax.SyntaxTree == currentSemanticModel.SyntaxTree
                        ? currentSemanticModel
                        : currentSemanticModel.Compilation.GetSemanticModel(syntax.SyntaxTree);
#pragma warning restore RS1030
                    var body = GetFunctionBody(syntax);
                    var bodyOperation = body is null
                        ? null
                        : semanticModel.GetOperation(body, context.CancellationToken);
                    if (bodyOperation is not null
                        && TryFindCapturedEventContextInDelegate(
                            bodyOperation,
                            context,
                            knownTypes,
                            visitedSymbols,
                            provenanceAnchor,
                            out capturedContext))
                    {
                        return true;
                    }
                }
            }

            capturedContext = null!;
            return false;
        }
        finally
        {
            visitedSymbols.Remove(method);
        }
    }

    private static bool TryFindCapturedEventContextInDelegate(
        IOperation root,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        HashSet<ISymbol>? visitedLocals,
        SyntaxNode? provenanceAnchor,
        out SyntaxNode capturedContext)
    {
        foreach (var operation in DescendantOperations(root))
        {
            if (ContainsReferenceOwnedByNestedAnonymousFunction(operation, root))
            {
                continue;
            }

            if (operation is IPropertyReferenceOperation property
                && (knownTypes.IsEventContextReference(property.Property.Type)
                    || knownTypes.IsEventContextContainer(property.Property.Type)
                        && IsProvenEventSymbolCapture(
                            property.Property,
                            property.Syntax,
                            context,
                            knownTypes,
                            provenanceAnchor)))
            {
                capturedContext = property.Syntax;
                return true;
            }

            if (operation is IParameterReferenceOperation parameterReference
                && (knownTypes.IsEventContextContainer(parameterReference.Parameter.Type)
                    || knownTypes.IsEventContextReference(parameterReference.Parameter.Type)))
            {
                capturedContext = parameterReference.Syntax;
                return true;
            }

            if (operation is IFieldReferenceOperation fieldReference
                && (knownTypes.IsEventContextReference(fieldReference.Field.Type)
                    || knownTypes.IsEventContextContainer(fieldReference.Field.Type)
                        && IsProvenEventSymbolCapture(
                            fieldReference.Field,
                            fieldReference.Syntax,
                            context,
                            knownTypes,
                            provenanceAnchor)))
            {
                capturedContext = fieldReference.Syntax;
                return true;
            }

            if (operation is IInvocationOperation invocation
                && invocation.TargetMethod.MethodKind is MethodKind.LocalFunction or MethodKind.Ordinary
                && invocation.TargetMethod.DeclaringSyntaxReferences.Length > 0)
            {
                visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                if (TryFindCapturedEventContextInMethod(
                    invocation.TargetMethod,
                    context,
                    knownTypes,
                    visitedLocals,
                    provenanceAnchor ?? invocation.Syntax,
                    out _))
                {
                    capturedContext = invocation.Syntax;
                    return true;
                }
            }

            if (operation is not ILocalReferenceOperation localReference)
            {
                continue;
            }

            visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (!visitedLocals.Add(localReference.Local))
            {
                continue;
            }

            if (TryGetStableInitializer(localReference, context, out var initializer)
                && initializer is not null
                && (localReference.Local.Type.TypeKind == TypeKind.Delegate
                    ? TryFindCapturedEventContext(
                        initializer,
                        context,
                        knownTypes,
                        visitedLocals,
                        out _)
                    : TryFindDeferredStateContext(
                        initializer,
                        context,
                        knownTypes,
                        visitedLocals,
                        out _)))
            {
                capturedContext = localReference.Syntax;
                return true;
            }

            if (ContainsEventContextReference(localReference.Local.Type, knownTypes)
                && IsProvenEventSymbolCapture(
                    localReference.Local,
                    localReference.Syntax,
                    context,
                    knownTypes,
                    provenanceAnchor))
            {
                capturedContext = localReference.Syntax;
                return true;
            }

            visitedLocals.Remove(localReference.Local);
        }

        capturedContext = null!;
        return false;
    }

    private static bool IsProvenEventSymbolCapture(
        ISymbol retainedSymbol,
        SyntaxNode reference,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        SyntaxNode? provenanceAnchor)
    {
        var anchor = provenanceAnchor ?? reference;
        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel is null || semanticModel.SyntaxTree != anchor.SyntaxTree)
        {
            return false;
        }

        var callback = anchor.AncestorsAndSelf()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .FirstOrDefault(candidate => candidate.FirstAncestorOrSelf<AssignmentExpressionSyntax>()
                    is { } assignment
                && semanticModel.GetSymbolInfo(
                        assignment.Left,
                        context.CancellationToken).Symbol
                    is IPropertySymbol callbackProperty
                && knownTypes.IsSynchronousCallback(callbackProperty));
        if (callback is null
            || semanticModel.GetOperation(callback, context.CancellationToken)
                is not IAnonymousFunctionOperation callbackOperation
            || callbackOperation.Symbol.Parameters.Length == 0)
        {
            return false;
        }

        var callbackParameters = new HashSet<ISymbol>(
            callbackOperation.Symbol.Parameters,
            SymbolEqualityComparer.Default);
        var flowTarget = anchor.AncestorsAndSelf()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .FirstOrDefault(candidate => candidate != callback
                && callback.Span.Contains(candidate.Span))
            ?? anchor;

        var writes = new List<(SyntaxNode Syntax, ExpressionSyntax Value)>();
        foreach (var candidate in callback.Body
                     .DescendantNodes(descendIntoChildren: static node =>
                node is not AnonymousFunctionExpressionSyntax
                    and not LocalFunctionStatementSyntax)
                     .Where(candidate => candidate.SpanStart < anchor.SpanStart))
        {
            if (candidate is AssignmentExpressionSyntax assignment
                && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(
                        assignment.Left,
                        context.CancellationToken).Symbol,
                    retainedSymbol))
            {
                writes.Add((assignment, assignment.Right));
            }
            else if (candidate is VariableDeclaratorSyntax
                     {
                         Initializer.Value: { } value,
                     } declarator
                && SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(
                        declarator,
                        context.CancellationToken),
                    retainedSymbol))
            {
                writes.Add((declarator, value));
            }
        }

        var origins = new List<SyntaxNode?>();
        var kills = new List<SyntaxNode>();
        foreach (var write in writes.OrderBy(static candidate => candidate.Syntax.SpanStart))
        {
            var value = semanticModel.GetOperation(
                write.Value,
                context.CancellationToken);
            if (value is not null
                && ContainsCallbackParameterInRetainedValue(
                    value,
                    callbackParameters,
                    knownTypes))
            {
                origins.Add(write.Syntax);
            }
            else
            {
                kills.Add(write.Syntax);
            }
        }

        var controlFlowGraph = TryCreateControlFlowGraph(
            callback.Body,
            semanticModel,
            context.CancellationToken);
        return origins.Count > 0
            && HasReachableOrigin(origins, flowTarget, controlFlowGraph, kills)
            && !IsClearedOnEveryBranch(origins, kills, flowTarget, callback.Body);
    }

    private static bool ContainsCallbackParameterInRetainedValue(
        IOperation operation,
        HashSet<ISymbol> callbackParameters,
        KnownTypes knownTypes)
    {
        operation = Unwrap(operation)!;
        if (operation is IParameterReferenceOperation parameterReference)
        {
            return callbackParameters.Contains(parameterReference.Parameter);
        }

        var retainedParts = GetRetainedValueParts(operation).ToArray();
        if (retainedParts.Length > 0)
        {
            return retainedParts.Any(part => ContainsCallbackParameterInRetainedValue(
                part,
                callbackParameters,
                knownTypes));
        }

        if (operation is IInvocationOperation invocation)
        {
            return invocation.Arguments.Any(argument =>
                ContainsCallbackParameterInRetainedValue(
                    argument.Value,
                    callbackParameters,
                    knownTypes));
        }

        if (!ContainsEventContextReference(operation.Type, knownTypes))
        {
            return false;
        }

        return operation.ChildOperations.Any(child =>
            ContainsCallbackParameterInRetainedValue(
                child,
                callbackParameters,
                knownTypes));
    }

    private static bool ContainsReferenceOwnedByNestedAnonymousFunction(
        IOperation operation,
        IOperation root)
    {
        for (var current = operation; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation anonymous
                && root.Syntax.Span.Contains(anonymous.Syntax.Span))
            {
                return ContainsAnonymousOwnedReference(operation, anonymous.Symbol);
            }
        }

        return false;
    }

    private static IEnumerable<IOperation> DescendantOperations(IOperation root)
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
        => TryGetInitializer(
            localReference,
            context,
            requireSingleUse: false,
            allowMemberMutation: false,
            out initializer);

    private static bool TryGetStableAliasInitializer(
        ILocalReferenceOperation localReference,
        OperationAnalysisContext context,
        out IOperation? initializer)
        => TryGetInitializer(
            localReference,
            context,
            requireSingleUse: false,
            allowMemberMutation: true,
            out initializer);

    private static bool TryGetStableLocalInitializer(
        ILocalSymbol local,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SyntaxNode? reference,
        out ExpressionSyntax initializer)
    {
        var declarations = local.DeclaringSyntaxReferences;
        if (declarations.Length != 1
            || declarations[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax
            {
                Initializer.Value: { } initializerSyntax,
            } declarator
            || semanticModel.SyntaxTree != declarator.SyntaxTree)
        {
            initializer = null!;
            return false;
        }

        var declarationScope = GetExecutableScope(declarator, cancellationToken);
        foreach (var identifier in declarationScope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText == local.Name
                && SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    local)
                && (reference is null || identifier.SpanStart < reference.SpanStart)
                && IsWritten(identifier))
            {
                initializer = null!;
                return false;
            }
        }

        initializer = initializerSyntax;
        return true;
    }

    private static bool TryGetSingleUseInitializer(
        ILocalReferenceOperation localReference,
        OperationAnalysisContext context,
        out IOperation? initializer)
        => TryGetInitializer(
            localReference,
            context,
            requireSingleUse: true,
            allowMemberMutation: false,
            out initializer);

    private static bool TryGetInitializer(
        ILocalReferenceOperation localReference,
        OperationAnalysisContext context,
        bool requireSingleUse,
        bool allowMemberMutation,
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
                if ((allowMemberMutation ? IsReassigned(identifier) : IsWritten(identifier))
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
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        out string? memberName)
    {
        var method = Normalize(invocation.TargetMethod);
        if (!IsKnownAsyncStrategyFactory(method, knownTypes))
        {
            memberName = null;
            return false;
        }

        if (method.Name == "Fallback")
        {
            memberName = "Fallback recovery delegate";
            return true;
        }

        if (method.Name == "UseRateLimiter")
        {
            memberName = "UseRateLimiter acquisition";
            return true;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == "configure"
                && TryFindAsyncConfiguration(argument.Value, context, knownTypes, out memberName))
            {
                return true;
            }
        }

        memberName = null;
        return false;
    }

    private static bool IsKnownAsyncStrategyFactory(IMethodSymbol method, KnownTypes knownTypes) =>
        IsKevlarFluentMethod(method, knownTypes)
        || (method.Name == "Behavior" && knownTypes.IsChaosShield(method.ContainingType));

    private static bool TryFindAsyncConfiguration(
        IOperation operation,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        out string? memberName)
    {
        operation = Unwrap(operation)!;
        if (operation is IDelegateCreationOperation delegateCreation)
        {
            operation = Unwrap(delegateCreation.Target)!;
        }

        if (operation is not IAnonymousFunctionOperation anonymousFunction
            || anonymousFunction.Symbol.Parameters.Length != 1)
        {
            memberName = null;
            return false;
        }

        if (HasStaticallyDisabledRetries(
                anonymousFunction,
                anonymousFunction.Symbol.Parameters[0],
                context)
            || (TryGetConfiguredMaxHedgedAttempts(operation, out var maxHedgedAttempts)
                && maxHedgedAttempts == 0)
            || HasStaticallyDisabledChaosBehavior(
                anonymousFunction,
                anonymousFunction.Symbol.Parameters[0],
                context))
        {
            memberName = null;
            return false;
        }

        return TryFindAsyncConfiguration(
            anonymousFunction.Body,
            anonymousFunction.Body,
            anonymousFunction.Symbol.Parameters[0],
            context,
            knownTypes,
            out memberName);
    }

    private static bool HasStaticallyDisabledChaosBehavior(
        IAnonymousFunctionOperation anonymousFunction,
        IParameterSymbol configuratorParameter,
        OperationAnalysisContext context)
    {
        if (configuratorParameter.Type is not INamedTypeSymbol
            {
                Name: "ChaosBehaviorOptions",
            } options
            || options.ContainingNamespace.ToDisplayString() != "Kevlar.Chaos")
        {
            return false;
        }

        if (TryGetFinalChaosPropertyValue(
                anonymousFunction,
                configuratorParameter,
                "Enabled",
                context,
                out var enabled)
            && (enabled is null
                || enabled.ConstantValue is { HasValue: true, Value: false }))
        {
            return true;
        }

        return TryGetFinalChaosPropertyValue(
                anonymousFunction,
                configuratorParameter,
                "InjectionRate",
                context,
                out var injectionRate)
            && injectionRate?.ConstantValue is { HasValue: true, Value: double rate }
            && rate <= 0
            && TryGetFinalChaosPropertyValue(
                anonymousFunction,
                configuratorParameter,
                "InjectionRateGenerator",
                context,
                out var injectionRateGenerator)
            && (injectionRateGenerator is null
                || injectionRateGenerator.ConstantValue is { HasValue: true, Value: null });
    }

    private static bool TryGetFinalChaosPropertyValue(
        IAnonymousFunctionOperation anonymousFunction,
        IParameterSymbol configuratorParameter,
        string propertyName,
        OperationAnalysisContext context,
        out IOperation? configuredValue)
    {
        configuredValue = null;
        var lastDirectAssignmentStart = -1;
        if (anonymousFunction.Body is IBlockOperation block)
        {
            foreach (var statement in block.Operations)
            {
                var operation = statement is IExpressionStatementOperation expressionStatement
                    ? expressionStatement.Operation
                    : statement;
                operation = Unwrap(operation)!;
                if (operation is IAssignmentOperation
                    {
                        Target: IPropertyReferenceOperation property,
                        Value: { } value,
                    }
                    && property.Property.Name == propertyName
                    && property.Instance is { } instance
                    && ReferencesConfiguratorParameter(instance, configuratorParameter, context))
                {
                    configuredValue = value;
                    lastDirectAssignmentStart = operation.Syntax.SpanStart;
                }
            }
        }

        return !HasBypassingControlFlowBefore(anonymousFunction.Body, lastDirectAssignmentStart)
            && !HasChaosPropertyAssignmentAfter(
            anonymousFunction.Body,
            configuratorParameter,
            propertyName,
            lastDirectAssignmentStart,
            context);
    }

    private static bool HasChaosPropertyAssignmentAfter(
        IOperation operation,
        IParameterSymbol configuratorParameter,
        string propertyName,
        int position,
        OperationAnalysisContext context)
    {
        if (operation.Syntax.SpanStart > position
            && operation is IAssignmentOperation
            {
                Target: IPropertyReferenceOperation property,
            }
            && property.Property.Name == propertyName
            && property.Instance is { } instance
            && ReferencesConfiguratorParameter(instance, configuratorParameter, context))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (child is not IAnonymousFunctionOperation
                && HasChaosPropertyAssignmentAfter(
                child,
                configuratorParameter,
                propertyName,
                position,
                context))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBypassingControlFlowBefore(IOperation operation, int position)
    {
        if (operation.Syntax.SpanStart >= position)
        {
            return false;
        }

        if (!operation.IsImplicit && operation is IReturnOperation or IBranchOperation)
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (child is not IAnonymousFunctionOperation
                && HasBypassingControlFlowBefore(child, position))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStaticallyDisabledRetries(
        IAnonymousFunctionOperation anonymousFunction,
        IParameterSymbol configuratorParameter,
        OperationAnalysisContext context)
    {
        if (configuratorParameter.Type is not INamedTypeSymbol retryOptions
            || retryOptions.Name != "RetryOptions"
            || retryOptions.ContainingNamespace.ToDisplayString() != "Kevlar"
            || anonymousFunction.Body is not IBlockOperation block)
        {
            return false;
        }

        int? maxRetries = null;
        var lastDirectAssignmentStart = -1;
        foreach (var statement in block.Operations)
        {
            var operation = statement is IExpressionStatementOperation expressionStatement
                ? expressionStatement.Operation
                : statement;
            operation = Unwrap(operation)!;
            if (operation is IAssignmentOperation
                {
                    Target: IPropertyReferenceOperation { Property.Name: "MaxRetries" } property,
                    Value: { } value,
                }
                && property.Instance is { } instance
                && ReferencesConfiguratorParameter(instance, configuratorParameter, context))
            {
                maxRetries = value.ConstantValue is { HasValue: true, Value: int configured }
                    ? configured
                    : null;
                lastDirectAssignmentStart = operation.Syntax.SpanStart;
            }
        }

        return maxRetries == 0
            && !HasBypassingControlFlowBefore(block, lastDirectAssignmentStart)
            && !HasMaxRetriesAssignmentAfter(
                block,
                configuratorParameter,
                lastDirectAssignmentStart,
                context);
    }

    private static bool HasMaxRetriesAssignmentAfter(
        IOperation operation,
        IParameterSymbol configuratorParameter,
        int position,
        OperationAnalysisContext context)
    {
        if (operation.Syntax.SpanStart > position
            && operation is IAssignmentOperation
            {
                Target: IPropertyReferenceOperation { Property.Name: "MaxRetries" } property,
            }
            && property.Instance is { } instance
            && ReferencesConfiguratorParameter(instance, configuratorParameter, context))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (child is not IAnonymousFunctionOperation
                && HasMaxRetriesAssignmentAfter(
                    child,
                    configuratorParameter,
                    position,
                    context))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindAsyncConfiguration(
        IOperation operation,
        IOperation configurationBody,
        IParameterSymbol configuratorParameter,
        OperationAnalysisContext context,
        KnownTypes knownTypes,
        out string? memberName)
    {
        operation = Unwrap(operation)!;
        if (operation is IAnonymousFunctionOperation)
        {
            memberName = null;
            return false;
        }

        if (operation is IAssignmentOperation
            {
                Target: IPropertyReferenceOperation propertyReference,
                Value: { } value,
            }
            && propertyReference.Instance is { } instance
            && ReferencesConfiguratorParameter(instance, configuratorParameter, context)
            && knownTypes.IsAsyncConfigurationProperty(propertyReference.Property)
            && value.ConstantValue is not { HasValue: true, Value: null }
            && !IsGuaranteedClearedAfter(
                configurationBody,
                operation,
                propertyReference.Property,
                configuratorParameter,
                context))
        {
            memberName = $"{propertyReference.Property.ContainingType.Name}.{propertyReference.Property.Name}";
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (TryFindAsyncConfiguration(
                child,
                configurationBody,
                configuratorParameter,
                context,
                knownTypes,
                out memberName))
            {
                return true;
            }
        }

        memberName = null;
        return false;
    }

    private static bool IsGuaranteedClearedAfter(
        IOperation configurationBody,
        IOperation configuredAssignment,
        IPropertySymbol property,
        IParameterSymbol configuratorParameter,
        OperationAnalysisContext context)
    {
        if (configurationBody is not IBlockOperation block)
        {
            return false;
        }

        IOperation? finalValue = null;
        var finalAssignmentStart = -1;
        foreach (var statement in block.Operations)
        {
            var operation = statement is IExpressionStatementOperation expressionStatement
                ? expressionStatement.Operation
                : statement;
            operation = Unwrap(operation)!;
            if (operation.Syntax.SpanStart > configuredAssignment.Syntax.SpanStart
                && operation is IAssignmentOperation
                {
                    Target: IPropertyReferenceOperation propertyReference,
                    Value: { } value,
                }
                && SymbolEqualityComparer.Default.Equals(propertyReference.Property, property)
                && propertyReference.Instance is { } instance
                && ReferencesConfiguratorParameter(instance, configuratorParameter, context))
            {
                finalValue = value;
                finalAssignmentStart = operation.Syntax.SpanStart;
            }
        }

        return finalValue?.ConstantValue is { HasValue: true, Value: null }
            && !HasBypassingControlFlowBefore(block, finalAssignmentStart)
            && !HasPropertyAssignmentAfter(
                block,
                property,
                configuratorParameter,
                finalAssignmentStart,
                context);
    }

    private static bool HasPropertyAssignmentAfter(
        IOperation operation,
        IPropertySymbol property,
        IParameterSymbol configuratorParameter,
        int position,
        OperationAnalysisContext context)
    {
        if (operation.Syntax.SpanStart > position
            && operation is IAssignmentOperation
            {
                Target: IPropertyReferenceOperation propertyReference,
            }
            && SymbolEqualityComparer.Default.Equals(propertyReference.Property, property)
            && propertyReference.Instance is { } instance
            && ReferencesConfiguratorParameter(instance, configuratorParameter, context))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (child is not IAnonymousFunctionOperation
                && HasPropertyAssignmentAfter(
                    child,
                    property,
                    configuratorParameter,
                    position,
                    context))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReassigned(IdentifierNameSyntax identifier)
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

    private static bool ReferencesConfiguratorParameter(
        IOperation operation,
        IParameterSymbol configuratorParameter,
        OperationAnalysisContext context,
        HashSet<ISymbol>? visitedLocals = null)
    {
        operation = Unwrap(operation)!;
        if (operation is IParameterReferenceOperation parameterReference)
        {
            return SymbolEqualityComparer.Default.Equals(
                parameterReference.Parameter,
                configuratorParameter);
        }

        if (operation is not ILocalReferenceOperation localReference)
        {
            return false;
        }

        visitedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (!visitedLocals.Add(localReference.Local)
            || !TryGetStableAliasInitializer(localReference, context, out var initializer)
            || initializer is null)
        {
            return false;
        }

        var referencesParameter = ReferencesConfiguratorParameter(
            initializer,
            configuratorParameter,
            context,
            visitedLocals);
        visitedLocals.Remove(localReference.Local);
        return referencesParameter;
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
        private readonly INamedTypeSymbol? _retryOptions;
        private readonly INamedTypeSymbol? _retryOptionsOfT;
        private readonly INamedTypeSymbol? _hedgeOptions;
        private readonly INamedTypeSymbol? _hedgeOptionsOfT;
        private readonly INamedTypeSymbol? _timeoutOptions;
        private readonly INamedTypeSymbol? _fallbackOptions;
        private readonly INamedTypeSymbol? _fallbackOptionsOfT;
        private readonly INamedTypeSymbol? _circuitBreakerOptions;
        private readonly INamedTypeSymbol? _circuitBreakerOptionsOfT;
        private readonly INamedTypeSymbol? _rateLimitOptions;
        private readonly INamedTypeSymbol? _concurrencyLimitOptions;
        private readonly INamedTypeSymbol? _chaosShield;
        private readonly INamedTypeSymbol? _chaosBehaviorOptions;
        private readonly INamedTypeSymbol? _rateLimiterAdapterOptions;
        private readonly INamedTypeSymbol? _kevlarContext;
        private readonly INamedTypeSymbol? _kevlarProperties;
        private readonly HashSet<INamedTypeSymbol> _synchronousCallbackTypes;
        private readonly HashSet<INamedTypeSymbol> _eventContextContainerTypes;

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
            var kevlarAssembly = _shield?.ContainingAssembly;
            _retryOptions = kevlarAssembly?.GetTypeByMetadataName("Kevlar.RetryOptions");
            _retryOptionsOfT = kevlarAssembly?.GetTypeByMetadataName("Kevlar.RetryOptions`1");
            _hedgeOptions = kevlarAssembly?.GetTypeByMetadataName("Kevlar.HedgeOptions");
            _hedgeOptionsOfT = kevlarAssembly?.GetTypeByMetadataName("Kevlar.HedgeOptions`1");
            _timeoutOptions = kevlarAssembly?.GetTypeByMetadataName("Kevlar.TimeoutOptions");
            _fallbackOptions = kevlarAssembly?.GetTypeByMetadataName("Kevlar.FallbackOptions");
            _fallbackOptionsOfT = kevlarAssembly?.GetTypeByMetadataName("Kevlar.FallbackOptions`1");
            _circuitBreakerOptions = kevlarAssembly?.GetTypeByMetadataName("Kevlar.CircuitBreakerOptions");
            _circuitBreakerOptionsOfT = kevlarAssembly?.GetTypeByMetadataName("Kevlar.CircuitBreakerOptions`1");
            _rateLimitOptions = kevlarAssembly?.GetTypeByMetadataName("Kevlar.RateLimitOptions");
            _concurrencyLimitOptions = kevlarAssembly?.GetTypeByMetadataName("Kevlar.ConcurrencyLimitOptions");
            _chaosShield = compilation.GetTypeByMetadataName("Kevlar.Chaos.ChaosShield");
            _chaosBehaviorOptions = _chaosShield?.ContainingAssembly.GetTypeByMetadataName(
                "Kevlar.Chaos.ChaosBehaviorOptions");
            _rateLimiterAdapterOptions = _shieldRateLimiterExtensions?.ContainingAssembly.GetTypeByMetadataName(
                "Kevlar.Extensions.RateLimiting.RateLimiterAdapterOptions");
            _kevlarContext = kevlarAssembly?.GetTypeByMetadataName("Kevlar.KevlarContext");
            _kevlarProperties = kevlarAssembly?.GetTypeByMetadataName("Kevlar.KevlarProperties");
            _synchronousCallbackTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            _eventContextContainerTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            AddSynchronousCallbackType(compilation, "Kevlar.RetryOptions");
            AddSynchronousCallbackType(compilation, "Kevlar.RetryOptions`1");
            AddSynchronousCallbackType(compilation, "Kevlar.TimeoutOptions");
            AddSynchronousCallbackType(compilation, "Kevlar.CircuitBreakerOptions");
            AddSynchronousCallbackType(compilation, "Kevlar.CircuitBreakerOptions`1");
            AddSynchronousCallbackType(compilation, "Kevlar.HedgeOptions");
            AddSynchronousCallbackType(compilation, "Kevlar.HedgeOptions`1");
            AddSynchronousCallbackType(compilation, "Kevlar.FallbackOptions");
            AddSynchronousCallbackType(compilation, "Kevlar.FallbackOptions`1");
            AddSynchronousCallbackType(compilation, "Kevlar.RateLimitOptions");
            AddSynchronousCallbackType(compilation, "Kevlar.ConcurrencyLimitOptions");
            AddSynchronousCallbackType(compilation, "Kevlar.Chaos.ChaosOptions");
            AddSynchronousCallbackType(compilation, "Kevlar.Chaos.ChaosBehaviorOptions");
            AddSynchronousCallbackType(compilation, "Kevlar.Chaos.ChaosFaultOptions");
            AddSynchronousCallbackType(compilation, "Kevlar.Chaos.ChaosLatencyOptions");
            AddSynchronousCallbackType(compilation, "Kevlar.Chaos.ChaosOutcomeOptions`1");
            AddSynchronousCallbackType(compilation, "Kevlar.Extensions.RateLimiting.RateLimiterAdapterOptions");
            var assemblyName = compilation.AssemblyName;
            IsTestAssembly = assemblyName is not null
                && (assemblyName.EndsWith(".Test", StringComparison.OrdinalIgnoreCase)
                    || assemblyName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));
        }

        internal bool IsTestAssembly { get; }

        internal bool IsSynchronousCallback(IPropertySymbol property) =>
            _synchronousCallbackTypes.Contains(property.ContainingType.OriginalDefinition)
            && TryGetSynchronousCallbackEventType(property, out _);

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

        internal bool IsChaosShield(INamedTypeSymbol type) => Is(type, _chaosShield);

        internal bool IsPartitionedShield(INamedTypeSymbol type) =>
            Is(type, _partitionedShield) || Is(type, _partitionedShieldOfT);

        internal bool IsAsyncConfigurationProperty(IPropertySymbol property)
        {
            var type = property.ContainingType;
            return property.Name switch
            {
                "OnRetryAsync" => Is(type, _retryOptions) || Is(type, _retryOptionsOfT),
                "DelayGeneratorAsync" =>
                    Is(type, _retryOptions)
                    || Is(type, _retryOptionsOfT)
                    || Is(type, _hedgeOptions)
                    || Is(type, _hedgeOptionsOfT),
                "OnTimeoutAsync" or "TimeoutGenerator" => Is(type, _timeoutOptions),
                "OnFallbackAsync" => Is(type, _fallbackOptions) || Is(type, _fallbackOptionsOfT),
                "OnStateChangedAsync" or "BreakDurationGenerator" =>
                    Is(type, _circuitBreakerOptions) || Is(type, _circuitBreakerOptionsOfT),
                "OnRejectedAsync" =>
                    Is(type, _rateLimitOptions)
                    || Is(type, _concurrencyLimitOptions)
                    || Is(type, _rateLimiterAdapterOptions),
                "Behavior" => Is(type, _chaosBehaviorOptions),
                _ => false,
            };
        }

        internal bool IsEventContextReference(ITypeSymbol? type) =>
            type is INamedTypeSymbol namedType
            && (Is(namedType, _kevlarContext)
                || Is(namedType, _kevlarProperties));

        internal bool IsEventContextContainer(ITypeSymbol? type) =>
            type is INamedTypeSymbol namedType
            && _eventContextContainerTypes.Contains(namedType.OriginalDefinition);

        internal bool IsNonGenericExecutionResult(ITypeSymbol type) =>
            type is INamedTypeSymbol namedType
            && (Is(namedType, _outcome)
                || Is(namedType, _task)
                || Is(namedType, _valueTask)
                || ((Is(namedType, _taskOfT) || Is(namedType, _valueTaskOfT))
                    && IsNonGenericExecutionResult(namedType.TypeArguments[0])));

        private void AddSynchronousCallbackType(Compilation compilation, string metadataName)
        {
            if (compilation.GetTypeByMetadataName(metadataName) is { } type)
            {
                _synchronousCallbackTypes.Add(type);
                foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
                {
                    if (TryGetSynchronousCallbackEventType(property, out var eventType))
                    {
                        _eventContextContainerTypes.Add(eventType.OriginalDefinition);
                    }
                }
            }
        }

        private bool TryGetSynchronousCallbackEventType(
            IPropertySymbol property,
            out INamedTypeSymbol eventType)
        {
            eventType = null!;
            if (property.Type is not INamedTypeSymbol callbackType)
            {
                return false;
            }

            if (callbackType.TypeKind != TypeKind.Delegate
                || callbackType.TypeArguments.Length == 0
                || callbackType.TypeArguments[0] is not INamedTypeSymbol callbackEventType
                || !IsContextBearingCallbackArgument(callbackEventType))
            {
                return false;
            }

            var isSynchronousAction = callbackType is
                {
                    Name: "Action",
                    Arity: 1,
                    ContainingNamespace.Name: "System",
                };
            var isSynchronousFunc = callbackType is
                {
                    Name: "Func",
                    ContainingNamespace.Name: "System",
                }
                && !IsTaskLike(callbackType.TypeArguments[callbackType.TypeArguments.Length - 1]);
            if (isSynchronousAction || isSynchronousFunc)
            {
                eventType = callbackEventType;
                return true;
            }

            return false;
        }

        private bool IsContextBearingCallbackArgument(INamedTypeSymbol type) =>
            IsEventContextReference(type)
            || type.GetMembers("Context")
                .OfType<IPropertySymbol>()
                .Any(property => !property.IsStatic && IsEventContextReference(property.Type));

        private static bool Is(INamedTypeSymbol type, INamedTypeSymbol? expected) =>
            expected is not null
            && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, expected);
    }
}
