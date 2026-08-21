namespace Kevlar.Testing;

/// <summary>Read-only retry configuration.</summary>
public sealed class RetryStrategyDescriptor : StrategyDescriptor
{
    internal RetryStrategyDescriptor(
        string description,
        int maxRetries,
        BackoffDescriptor backoff,
        TimeSpan? maxDelay,
        bool hasDelayGenerator,
        bool hasNotification)
        : base(StrategyKind.Retry, description)
    {
        MaxRetries = maxRetries;
        Backoff = backoff;
        MaxDelay = maxDelay;
        HasDelayGenerator = hasDelayGenerator;
        HasNotification = hasNotification;
    }

    /// <summary>Maximum retries after the initial attempt.</summary>
    public int MaxRetries { get; }

    /// <summary>An inert snapshot of the backoff configuration.</summary>
    public BackoffDescriptor Backoff { get; }

    /// <summary>The absolute delay cap, when configured.</summary>
    public TimeSpan? MaxDelay { get; }

    /// <summary>Whether a synchronous or asynchronous delay generator is configured.</summary>
    public bool HasDelayGenerator { get; }

    /// <summary>Whether a synchronous or asynchronous retry notification is configured.</summary>
    public bool HasNotification { get; }
}
