namespace Kevlar;

/// <summary>Describes an execution rejected by the built-in concurrency limiter.</summary>
public readonly struct ConcurrencyLimitRejectedEvent
{
    private readonly KevlarContext? _context;

    internal ConcurrencyLimitRejectedEvent(
        int maxConcurrency,
        int queueLimit,
        KevlarContext context)
    {
        MaxConcurrency = maxConcurrency;
        QueueLimit = queueLimit;
        _context = context;
    }

    /// <summary>The configured maximum concurrent executions.</summary>
    public int MaxConcurrency { get; }

    /// <summary>The configured maximum wait queue size.</summary>
    public int QueueLimit { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it after synchronous and
    /// asynchronous rejection callbacks complete.
    /// </summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);
}
