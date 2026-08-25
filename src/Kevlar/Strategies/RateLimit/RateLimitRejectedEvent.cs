namespace Kevlar;

/// <summary>Describes an execution rejected by the built-in rate limiter.</summary>
public readonly struct RateLimitRejectedEvent
{
    private readonly KevlarContext? _context;

    internal RateLimitRejectedEvent(
        TimeSpan? retryAfter,
        int permits,
        TimeSpan window,
        int burst,
        int queueLimit,
        KevlarContext context)
    {
        RetryAfter = retryAfter;
        Permits = permits;
        Window = window;
        Burst = burst;
        QueueLimit = queueLimit;
        _context = context;
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

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it after synchronous and
    /// asynchronous rejection callbacks complete.
    /// </summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);
}
