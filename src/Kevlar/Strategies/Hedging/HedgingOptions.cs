namespace Kevlar;

/// <summary>
/// Configuration for a hedging strategy: launch up to <see cref="MaxAttempts"/> concurrent
/// attempts, staggered by <see cref="Delay"/>, and return the first acceptable outcome.
/// A handled failure always launches the next attempt immediately.
/// </summary>
/// <remarks>
/// The executed delegate may be invoked multiple times concurrently — it must be safe to do so.
/// Hedging requires asynchronous execution.
/// </remarks>
public sealed class HedgingOptions
{
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

    /// <summary>The ambient execution context.</summary>
    public KevlarContext Context { get; }
}
