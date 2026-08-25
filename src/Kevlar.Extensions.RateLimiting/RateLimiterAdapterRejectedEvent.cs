namespace Kevlar.Extensions.RateLimiting;

/// <summary>Describes an execution rejected by an adapted rate limiter.</summary>
public readonly struct RateLimiterAdapterRejectedEvent
{
    internal RateLimiterAdapterRejectedEvent(
        TimeSpan? retryAfter,
        IReadOnlyDictionary<string, object?> metadata,
        int permitCount,
        int strategyIndex,
        KevlarContext context)
    {
        RetryAfter = retryAfter;
        Metadata = metadata;
        PermitCount = permitCount;
        StrategyIndex = strategyIndex;
        Context = context;
    }

    /// <summary>The lease-provided delay before another acquisition should be attempted.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>An immutable snapshot of all metadata supplied by the rejected lease.</summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; }

    /// <summary>The number of permits requested for the execution.</summary>
    public int PermitCount { get; }

    /// <summary>The zero-based position of this strategy in the executing shield.</summary>
    public int StrategyIndex { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it after synchronous and
    /// asynchronous rejection callbacks complete.
    /// </summary>
    public KevlarContext Context { get; }
}
