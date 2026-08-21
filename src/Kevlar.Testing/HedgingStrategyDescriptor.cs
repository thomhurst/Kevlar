namespace Kevlar.Testing;

/// <summary>Read-only hedging configuration.</summary>
public sealed class HedgingStrategyDescriptor : StrategyDescriptor
{
    internal HedgingStrategyDescriptor(
        string description,
        int maxAttempts,
        TimeSpan delay,
        bool hasNotification)
        : base(StrategyKind.Hedging, description)
    {
        MaxAttempts = maxAttempts;
        Delay = delay;
        HasNotification = hasNotification;
    }

    /// <summary>The maximum total attempts, including the primary.</summary>
    public int MaxAttempts { get; }

    /// <summary>The stagger delay between attempts.</summary>
    public TimeSpan Delay { get; }

    /// <summary>Whether a hedge notification is configured.</summary>
    public bool HasNotification { get; }
}
