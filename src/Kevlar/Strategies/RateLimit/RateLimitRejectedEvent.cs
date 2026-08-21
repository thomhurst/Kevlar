namespace Kevlar;

/// <summary>Describes an execution rejected by the built-in rate limiter.</summary>
public readonly struct RateLimitRejectedEvent
{
    internal RateLimitRejectedEvent(
        TimeSpan? retryAfter,
        int permits,
        TimeSpan window,
        int burst,
        int queueLimit,
        int strategyIndex,
        KevlarContext context)
    {
        RetryAfter = retryAfter;
        Permits = permits;
        Window = window;
        Burst = burst;
        QueueLimit = queueLimit;
        StrategyIndex = strategyIndex;
        Context = context;
    }

    /// <summary>The estimated delay before another execution can be admitted, when available.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>The configured permits replenished per <see cref="Window"/>.</summary>
    public int Permits { get; }

    /// <summary>The configured replenishment window.</summary>
    public TimeSpan Window { get; }

    /// <summary>The configured maximum burst capacity.</summary>
    public int Burst { get; }

    /// <summary>The configured maximum wait queue size.</summary>
    public int QueueLimit { get; }

    /// <summary>The zero-based position of this strategy in the executing shield.</summary>
    public int StrategyIndex { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it after synchronous and
    /// asynchronous rejection callbacks complete.
    /// </summary>
    public KevlarContext Context { get; }
}
