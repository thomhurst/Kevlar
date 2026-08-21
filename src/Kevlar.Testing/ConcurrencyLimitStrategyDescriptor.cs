namespace Kevlar.Testing;

/// <summary>Read-only concurrency limiter configuration.</summary>
public sealed class ConcurrencyLimitStrategyDescriptor : StrategyDescriptor
{
    internal ConcurrencyLimitStrategyDescriptor(
        string description,
        int maxConcurrency,
        int maxQueue,
        bool hasNotification)
        : base(StrategyKind.ConcurrencyLimit, description)
    {
        MaxConcurrency = maxConcurrency;
        MaxQueue = maxQueue;
        HasNotification = hasNotification;
    }

    /// <summary>The maximum concurrent executions.</summary>
    public int MaxConcurrency { get; }

    /// <summary>The maximum wait queue size.</summary>
    public int MaxQueue { get; }

    /// <summary>Whether synchronous or asynchronous rejection notifications are configured.</summary>
    public bool HasNotification { get; }
}
