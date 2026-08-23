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
    internal virtual bool InvokesContinuationAtMostOnce => false;

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

    /// <summary>Marks fallback strategies for chain-order validation.</summary>
    internal virtual bool IsFallback => false;

    /// <summary>
    /// Gets whether reusing this strategy instance within one chain is unsafe.
    /// Stateful custom strategies should override this property and return <see langword="true"/>
    /// when duplicate use could deadlock or corrupt their accounting.
    /// </summary>
    protected internal virtual bool IsDuplicateReferenceUnsafe => false;
}

internal interface IFallbackStrategyInspection
{
    Type? ResultType { get; }

    bool HasNotification { get; }
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
        ValueTask<Outcome<T>> execution;

        try
        {
            context.StrategyIndex = node.Index;
            execution = node.Strategy.ExecuteAsync(
                new Continuation<T, TState>(node.Next, _callback!, _state),
                context);
        }
        catch (Exception exception)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(exception));
        }

        return execution.IsCompletedSuccessfully
            ? execution
            : AwaitStrategyAsync(execution);
    }

    private static async ValueTask<Outcome<T>> AwaitStrategyAsync(ValueTask<Outcome<T>> execution)
    {
        try
        {
            return await execution.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Outcome<T>.FromException(exception);
        }
    }
}

/// <summary>A node in a shield's immutable strategy chain.</summary>
public sealed class StrategyNode
{
    internal StrategyNode(Strategy strategy, StrategyNode? next, int index)
    {
        Strategy = strategy;
        Next = next;
        Index = index;
    }

    internal Strategy Strategy { get; }

    internal StrategyNode? Next { get; }

    internal int Index { get; }
}
