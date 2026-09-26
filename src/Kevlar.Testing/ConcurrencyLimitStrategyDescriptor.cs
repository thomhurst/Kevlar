namespace Kevlar.Testing;

/// <summary>Read-only concurrency limiter configuration.</summary>
public sealed class ConcurrencyLimitStrategyDescriptor : StrategyDescriptor
{
    internal ConcurrencyLimitStrategyDescriptor(
        string description,
        int maxConcurrency,
        int queueLimit,
        TimeSpan? queueTimeout,
        bool hasNotification,
        bool usePriorityQueue = false)
        : base(StrategyKind.ConcurrencyLimit, description)
    {
        MaxConcurrency = maxConcurrency;
        QueueLimit = queueLimit;
        QueueTimeout = queueTimeout;
        HasNotification = hasNotification;
        UsePriorityQueue = usePriorityQueue;
    }

    /// <summary>The maximum concurrent executions.</summary>
    public int MaxConcurrency { get; }

    /// <summary>The maximum wait queue size.</summary>
    public int QueueLimit { get; }

    /// <summary>The maximum queue residence time, or null for an unbounded wait.</summary>
    public TimeSpan? QueueTimeout { get; }

    /// <summary>Whether synchronous or asynchronous rejection notifications are configured.</summary>
    public bool HasNotification { get; }

    /// <summary>Whether admission uses highest priority first, FIFO ties, and lower-priority eviction.</summary>
    public bool UsePriorityQueue { get; }
}
