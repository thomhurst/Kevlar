namespace Kevlar.Testing;

/// <summary>Read-only timeout configuration.</summary>
public sealed class TimeoutStrategyDescriptor : StrategyDescriptor
{
    internal TimeoutStrategyDescriptor(
        string description,
        TimeSpan timeout,
        bool hasTimeoutGenerator,
        bool hasNotification)
        : base(StrategyKind.Timeout, description)
    {
        Timeout = timeout;
        HasTimeoutGenerator = hasTimeoutGenerator;
        HasNotification = hasNotification;
    }

    /// <summary>The fixed timeout value, used when no generator is configured.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Whether a dynamic timeout generator is configured.</summary>
    public bool HasTimeoutGenerator { get; }

    /// <summary>Whether a synchronous or asynchronous timeout notification is configured.</summary>
    public bool HasNotification { get; }
}
