namespace Kevlar;

/// <summary>Describes an execution rejected by the built-in concurrency limiter.</summary>
public readonly struct ConcurrencyLimitRejectedEvent
{
    internal ConcurrencyLimitRejectedEvent(
        int maxConcurrency,
        int maxQueue,
        int strategyIndex,
        KevlarContext context)
    {
        MaxConcurrency = maxConcurrency;
        MaxQueue = maxQueue;
        StrategyIndex = strategyIndex;
        Context = context;
    }

    /// <summary>The configured maximum concurrent executions.</summary>
    public int MaxConcurrency { get; }

    /// <summary>The configured maximum wait queue size.</summary>
    public int MaxQueue { get; }

    /// <summary>The zero-based position of this strategy in the executing shield.</summary>
    public int StrategyIndex { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it after synchronous and
    /// asynchronous rejection callbacks complete.
    /// </summary>
    public KevlarContext Context { get; }
}
