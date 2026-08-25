namespace Kevlar.Extensions.RateLimiting;

/// <summary>Describes an execution rejected by an adapted rate limiter.</summary>
public readonly struct RateLimiterAdapterRejectedEvent
{
    private readonly KevlarContext? _context;

    internal RateLimiterAdapterRejectedEvent(
        TimeSpan? retryAfter,
        IReadOnlyDictionary<string, object?> metadata,
        int permitCount,
        KevlarContext context)
    {
        RetryAfter = retryAfter;
        Metadata = metadata;
        PermitCount = permitCount;
        _context = context;
    }

    /// <summary>The lease-provided delay before another acquisition should be attempted.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>An immutable snapshot of all metadata supplied by the rejected lease.</summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; }

    /// <summary>The number of permits requested for the execution.</summary>
    public int PermitCount { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it after synchronous and
    /// asynchronous rejection callbacks complete.
    /// </summary>
    public KevlarContext Context => _context
        ?? throw new InvalidOperationException(
            "A default strategy event has no execution context.");
}
