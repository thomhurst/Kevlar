namespace Kevlar.Testing;

/// <summary>Read-only rate limiter configuration.</summary>
public sealed class RateLimitStrategyDescriptor : StrategyDescriptor
{
    internal RateLimitStrategyDescriptor(
        string description,
        int permits,
        TimeSpan window,
        int burst,
        int queueLimit,
        TimeSpan? queueTimeout,
        bool hasNotification,
        bool usePriorityQueue = false)
        : base(StrategyKind.RateLimit, description)
    {
        Permits = permits;
        Window = window;
        Burst = burst;
        QueueLimit = queueLimit;
        QueueTimeout = queueTimeout;
        HasNotification = hasNotification;
        UsePriorityQueue = usePriorityQueue;
    }

    /// <summary>Permits replenished per window.</summary>
    public int Permits { get; }

    /// <summary>The replenishment window.</summary>
    public TimeSpan Window { get; }

    /// <summary>The maximum burst capacity.</summary>
    public int Burst { get; }

    /// <summary>The maximum wait queue size.</summary>
    public int QueueLimit { get; }

    /// <summary>The maximum queue residence time, or null for an unbounded wait.</summary>
    public TimeSpan? QueueTimeout { get; }

    /// <summary>Whether synchronous or asynchronous rejection notifications are configured.</summary>
    public bool HasNotification { get; }

    /// <summary>Whether admission uses highest priority first, FIFO ties, and lower-priority eviction.</summary>
    public bool UsePriorityQueue { get; }
}
