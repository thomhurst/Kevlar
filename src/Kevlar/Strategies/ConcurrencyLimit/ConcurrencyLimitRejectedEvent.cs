namespace Kevlar;

/// <summary>Describes an execution rejected by the built-in concurrency limiter.</summary>
public readonly struct ConcurrencyLimitRejectedEvent
{
    private readonly KevlarContext? _context;

    internal ConcurrencyLimitRejectedEvent(
        int maxConcurrency,
        int queueLimit,
        KevlarContext context,
        string? reason = null)
    {
        MaxConcurrency = maxConcurrency;
        QueueLimit = queueLimit;
        _context = context;
        Reason = reason;
    }

    /// <summary>The admission limit at rejection: configured capacity for a static limiter or the current adaptive limit.</summary>
    public int MaxConcurrency { get; }

    /// <summary>The configured maximum wait queue size.</summary>
    public int QueueLimit { get; }

    /// <summary>The rejection reason, such as <c>queue_timeout</c>, or null for capacity rejection.</summary>
    public string? Reason { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it after synchronous and
    /// asynchronous rejection callbacks complete.
    /// </summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);
}
