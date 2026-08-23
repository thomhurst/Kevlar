namespace Kevlar;

/// <summary>
/// Configuration for a hedging strategy: launch up to <see cref="MaxAttempts"/> concurrent
/// attempts, staggered by <see cref="Delay"/>, and return the first acceptable outcome.
/// A handled failure always launches the next attempt immediately.
/// </summary>
/// <remarks>
/// The executed delegate may be invoked multiple times concurrently — it must be safe to do so.
/// Hedging requires asynchronous execution.
/// Before an additional attempt starts, callbacks run in this order: <see cref="OnHedge"/>,
/// <see cref="OnHedgeAsync"/>, then <see cref="ActionGenerator"/>. Caller cancellation is checked
/// before callbacks and again before the generated operation starts.
/// </remarks>
public class HedgeOptions
{
    /// <summary>
    /// Locally selects exceptions handled by this hedging strategy. When set, this strategy
    /// ignores the ambient handling clause and handles only outcomes selected locally.
    /// </summary>
    public Func<Exception, bool>? HandlesException { get; set; }

    internal virtual bool HasHandlingOverride => HandlesException is not null;

    /// <summary>Total attempts including the original. Default 2.</summary>
    public int MaxAttempts { get; set; } = 2;

    /// <summary>
    /// Time to wait before launching the next attempt while the current ones are still running.
    /// <see cref="TimeSpan.Zero"/> launches all attempts at once;
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> hedges only on failure.
    /// Default 1 second.
    /// </summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Invoked when an additional hedged attempt is launched.</summary>
    public Action<HedgeEvent>? OnHedge { get; set; }

    /// <summary>Invoked and awaited after <see cref="OnHedge"/> and before an additional attempt starts.</summary>
    public Func<HedgeEvent, ValueTask>? OnHedgeAsync { get; set; }

    /// <summary>
    /// Selects a replacement operation for each additional attempt. A <see langword="null"/>
    /// result runs the original operation. Create the generator with the result type used to
    /// execute the shield; use the void factory for void executions.
    /// </summary>
    public HedgeActionGenerator? ActionGenerator { get; set; }
}

/// <summary>Describes a hedged attempt being launched.</summary>
public readonly struct HedgeEvent
{
    internal HedgeEvent(int attempt, KevlarContext context)
    {
        Attempt = attempt;
        Context = context;
    }

    /// <summary>The 1-based number of the attempt being launched (2 = first hedge).</summary>
    public int Attempt { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it after synchronous and
    /// asynchronous hedge callbacks complete.
    /// </summary>
    public KevlarContext Context { get; }
}
