namespace Kevlar.Testing;

/// <summary>Read-only concurrency limiter configuration.</summary>
public sealed class ConcurrencyLimitStrategyDescriptor : StrategyDescriptor
{
    internal ConcurrencyLimitStrategyDescriptor(
        string description,
        int maxConcurrency,
        int queueLimit,
        bool hasNotification)
        : base(StrategyKind.ConcurrencyLimit, description)
    {
        MaxConcurrency = maxConcurrency;
        QueueLimit = queueLimit;
        HasNotification = hasNotification;
    }

    /// <summary>The maximum concurrent executions.</summary>
    public int MaxConcurrency { get; }

    /// <summary>The maximum wait queue size.</summary>
    public int QueueLimit { get; }

    /// <summary>Whether synchronous or asynchronous rejection notifications are configured.</summary>
    public bool HasNotification { get; }
}
