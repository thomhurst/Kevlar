using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// Base class for resilience strategies. A strategy is middleware: it receives a
/// <see cref="Continuation{T, TState}"/> representing the rest of the pipeline and may invoke it
/// zero, one or many times.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be thread-safe: a single strategy instance is shared by every execution
/// of the shield that contains it, and by every shield it is composed into. Strategy-local state
/// (circuit breaker counters, rate limiter tokens, concurrency limit slots) is intentionally shared this way.
/// </para>
/// <para>
/// Strategies should communicate failures by returning <see cref="Outcome{T}.FromException"/>
/// rather than throwing, so that outer strategies can observe and handle them. Exceptions that do
/// escape a strategy are converted to outcomes by the pipeline.
/// </para>
/// </remarks>
public abstract class Strategy
{
    private StrategyExecutionTracker? _executionTracker;
    private WeakReference<object>? _shieldOwner;

    /// <summary>
    /// Gets whether this strategy guarantees invoking its continuation at most once per execution.
    /// </summary>
    /// <remarks>
    /// Override and return <see langword="true"/> only when every path invokes <c>next</c> zero or
    /// one time. Strategies that retry, hedge, loop, or otherwise may invoke <c>next</c> more than
    /// once must retain the conservative default.
    /// </remarks>
    protected internal virtual bool InvokesContinuationAtMostOnce => false;

    internal virtual bool RequiresContinuationOverlapIsolation => !InvokesContinuationAtMostOnce;

    /// <summary>
    /// The handling clause this reactive strategy acts on, or <see langword="null"/> for a
    /// proactive strategy. Override this when the strategy receives a clause through a
    /// <c>Use</c> factory so chain validation and testing descriptors can inspect it.
    /// </summary>
    protected virtual HandlingClause? Handling => null;

    /// <summary>
    /// Executes the strategy around the rest of the pipeline.
    /// </summary>
    /// <typeparam name="T">The result type of the execution.</typeparam>
    /// <typeparam name="TState">Caller-supplied state threaded through the pipeline without allocation.</typeparam>
    /// <param name="next">The remainder of the pipeline, ending in the user's delegate.</param>
    /// <param name="context">The ambient execution context.</param>
    public abstract ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context);

    /// <summary>
    /// A one-line human-readable summary of this strategy and its configuration, used by
    /// <see cref="Shield.ToString"/> to describe a whole pipeline. Defaults to the type name.
    /// </summary>
    public virtual string Describe() => GetType().Name;

    /// <summary>The handling clause this reactive strategy acts on; null for proactive strategies.</summary>
    internal virtual OutcomeJudge? ReactiveJudge => Handling?.Judge;

    /// <summary>Whether this reactive strategy replaces ambient handling with local predicates.</summary>
    internal virtual bool HasHandlingOverride => false;

    /// <summary>Marks fallback strategies for chain-order validation.</summary>
    internal virtual bool IsFallback => false;

    /// <summary>
    /// Gets whether reusing this strategy instance within one chain is unsafe.
    /// Stateful custom strategies should override this property and return <see langword="true"/>
    /// when duplicate use could deadlock or corrupt their accounting.
    /// </summary>
    protected internal virtual bool IsDuplicateReferenceUnsafe => false;

    internal StrategyExecutionTracker EnableExecutionTracking()
    {
        var tracker = Volatile.Read(ref _executionTracker);
        if (tracker is not null)
        {
            return tracker;
        }

        var created = new StrategyExecutionTracker();
        return Interlocked.CompareExchange(ref _executionTracker, created, null) ?? created;
    }

    internal StrategyExecutionTracker? ExecutionTracker => Volatile.Read(ref _executionTracker);

    internal object GetShieldOwner()
    {
        while (true)
        {
            var reference = Volatile.Read(ref _shieldOwner);
            if (reference is not null && reference.TryGetTarget(out var owner))
            {
                return owner;
            }

            owner = new object();
            var replacement = new WeakReference<object>(owner);
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _shieldOwner, replacement, reference),
                    reference))
            {
                return owner;
            }
        }
    }

    internal bool HasShieldOwner() =>
        Volatile.Read(ref _shieldOwner)?.TryGetTarget(out _) == true;
}

internal sealed class StrategyExecutionTracker
{
    private int _activeExecutions;

    public int ActiveExecutions => Volatile.Read(ref _activeExecutions);

    public void Enter() => Interlocked.Increment(ref _activeExecutions);

    public void Exit() => Interlocked.Decrement(ref _activeExecutions);
}

internal interface IFallbackStrategyInspection
{
    Type? ResultType { get; }

    bool HasNotification { get; }
}

internal interface IShieldLifecycle
{
    Strategy[] Strategies { get; }
}

/// <summary>
/// The rest of a shield pipeline, from a strategy's point of view. Invoking it runs every
/// remaining strategy and finally the user's delegate. It may be invoked multiple times
/// (retries, hedging) and with a forked context (hedging).
/// </summary>
public readonly struct Continuation<T, TState>
{
    private readonly StrategyNode? _next;
    private readonly Func<TState, KevlarContext, ValueTask<Outcome<T>>>? _callback;
    private readonly TState _state;

    internal Continuation(StrategyNode? next, Func<TState, KevlarContext, ValueTask<Outcome<T>>> callback, TState state)
    {
        _next = next;
        _callback = callback;
        _state = state;
    }

    /// <summary>
    /// Runs the remainder of the pipeline. Never throws; failures are returned as outcomes.
    /// A default, uninitialized continuation returns an <see cref="InvalidOperationException"/> outcome.
    /// </summary>
    public ValueTask<Outcome<T>> InvokeAsync(KevlarContext context)
    {
        if (_callback is null)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(
                new InvalidOperationException("The continuation is not initialized.")));
        }

        if (_next is null)
        {
            return _callback(_state, context);
        }

        return InvokeStrategyAsync(_next, context);
    }

    private ValueTask<Outcome<T>> InvokeStrategyAsync(StrategyNode node, KevlarContext context)
    {
        if (node.RequiresOverlapIsolation && !context.TryEnterStrategy(node.Index))
        {
            return InvokeStrategyWithForkAsync(node, context);
        }

        ValueTask<Outcome<T>> execution;
        var previousStrategyIndex = node.Index - 1;
        var executionTracker = node.Strategy.ExecutionTracker;
        executionTracker?.Enter();

        try
        {
            context.StrategyIndex = node.Index;
            execution = node.Strategy.ExecuteAsync(
                new Continuation<T, TState>(node.Next, _callback!, _state),
                context);
        }
        catch (Exception exception)
        {
            context.StrategyIndex = previousStrategyIndex;
            ExitStrategyIfRequired(node, context);
            executionTracker?.Exit();
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(exception));
        }

        if (execution.IsCompletedSuccessfully)
        {
            context.StrategyIndex = previousStrategyIndex;
            ExitStrategyIfRequired(node, context);
            executionTracker?.Exit();
            return execution;
        }

        return AwaitStrategyAsync(execution, context, previousStrategyIndex, node, executionTracker);
    }

    private async ValueTask<Outcome<T>> InvokeStrategyWithForkAsync(
        StrategyNode node,
        KevlarContext context)
    {
        var fork = context.Fork(context.CancellationToken);
        try
        {
            return await InvokeStrategyAsync(node, fork).ConfigureAwait(false);
        }
        finally
        {
            KevlarContext.Return(fork);
        }
    }

    private static async ValueTask<Outcome<T>> AwaitStrategyAsync(
        ValueTask<Outcome<T>> execution,
        KevlarContext context,
        int previousStrategyIndex,
        StrategyNode node,
        StrategyExecutionTracker? executionTracker)
    {
        try
        {
            return await execution.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Outcome<T>.FromException(exception);
        }
        finally
        {
            context.StrategyIndex = previousStrategyIndex;
            ExitStrategyIfRequired(node, context);
            executionTracker?.Exit();
        }
    }

    private static void ExitStrategyIfRequired(StrategyNode node, KevlarContext context)
    {
        if (node.RequiresOverlapIsolation)
        {
            context.ExitStrategy(node.Index);
        }
    }
}

internal sealed class StrategyNode
{
    internal StrategyNode(
        Strategy strategy,
        StrategyNode? next,
        int index,
        bool requiresOverlapIsolation)
    {
        Strategy = strategy;
        Next = next;
        Index = index;
        RequiresOverlapIsolation = requiresOverlapIsolation;
    }

    internal Strategy Strategy { get; }

    internal StrategyNode? Next { get; }

    internal int Index { get; }

    internal bool RequiresOverlapIsolation { get; }
}
