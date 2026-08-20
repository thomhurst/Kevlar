namespace Kevlar;

/// <summary>
/// Base class for resilience strategies. A strategy is middleware: it receives a
/// <see cref="Continuation{T, TState}"/> representing the rest of the pipeline and may invoke it
/// zero, one or many times.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be thread-safe: a single strategy instance is shared by every execution
/// of the policy that contains it, and by every policy it is composed into. Strategy-local state
/// (circuit breaker counters, rate limiter tokens, bulkhead slots) is intentionally shared this way.
/// </para>
/// <para>
/// Strategies should communicate failures by returning <see cref="Outcome{T}.FromException"/>
/// rather than throwing, so that outer strategies can observe and handle them. Exceptions that do
/// escape a strategy are converted to outcomes by the pipeline.
/// </para>
/// </remarks>
public abstract class Strategy
{
    /// <summary>
    /// Executes the strategy around the rest of the pipeline.
    /// </summary>
    /// <typeparam name="T">The result type of the execution.</typeparam>
    /// <typeparam name="TState">Caller-supplied state threaded through the pipeline without allocation.</typeparam>
    /// <param name="next">The remainder of the pipeline, ending in the user's delegate.</param>
    /// <param name="context">The ambient execution context.</param>
    public abstract ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context);
}

/// <summary>
/// The rest of a policy pipeline, from a strategy's point of view. Invoking it runs every
/// remaining strategy and finally the user's delegate. It may be invoked multiple times
/// (retries, hedging) and with a forked context (hedging).
/// </summary>
public readonly struct Continuation<T, TState>
{
    private readonly StrategyNode? _next;
    private readonly Func<TState, KevlarContext, ValueTask<Outcome<T>>> _callback;
    private readonly TState _state;

    internal Continuation(StrategyNode? next, Func<TState, KevlarContext, ValueTask<Outcome<T>>> callback, TState state)
    {
        _next = next;
        _callback = callback;
        _state = state;
    }

    /// <summary>Runs the remainder of the pipeline. Never throws; failures are returned as outcomes.</summary>
    public ValueTask<Outcome<T>> InvokeAsync(KevlarContext context)
    {
        if (_next is null)
        {
            return _callback(_state, context);
        }

        return InvokeStrategyAsync(_next, context);
    }

    private async ValueTask<Outcome<T>> InvokeStrategyAsync(StrategyNode node, KevlarContext context)
    {
        try
        {
            return await node.Strategy
                .ExecuteAsync(new Continuation<T, TState>(node.Next, _callback, _state), context)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Outcome<T>.FromException(exception);
        }
    }
}

/// <summary>A node in a policy's immutable strategy chain.</summary>
public sealed class StrategyNode
{
    internal StrategyNode(Strategy strategy, StrategyNode? next)
    {
        Strategy = strategy;
        Next = next;
    }

    internal Strategy Strategy { get; }

    internal StrategyNode? Next { get; }
}
