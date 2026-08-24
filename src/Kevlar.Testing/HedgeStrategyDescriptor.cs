namespace Kevlar.Testing;

/// <summary>Read-only hedging configuration.</summary>
public sealed class HedgeStrategyDescriptor : StrategyDescriptor
{
    internal HedgeStrategyDescriptor(
        string description,
        int maxAttempts,
        TimeSpan delay,
        bool hasNotification,
        bool hasHandlingOverride)
        : base(StrategyKind.Hedge, description)
    {
        MaxAttempts = maxAttempts;
        Delay = delay;
        HasNotification = hasNotification;
        HasHandlingOverride = hasHandlingOverride;
    }

    /// <summary>The maximum total attempts, including the primary.</summary>
    public int MaxAttempts { get; }

    /// <summary>The stagger delay between attempts.</summary>
    public TimeSpan Delay { get; }

    /// <summary>Whether a hedge notification is configured.</summary>
    public bool HasNotification { get; }

    /// <summary>Whether local predicates replace the ambient handling clause.</summary>
    public bool HasHandlingOverride { get; }
}
