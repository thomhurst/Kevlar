namespace Kevlar.Testing;

/// <summary>Read-only hedging configuration.</summary>
public sealed class HedgeStrategyDescriptor : StrategyDescriptor
{
    internal HedgeStrategyDescriptor(
        string description,
        int maxHedgedAttempts,
        TimeSpan delay,
        bool hasDelayGenerator,
        bool hasNotification,
        bool hasActionGenerator,
        bool hasHandlingOverride)
        : base(StrategyKind.Hedge, description)
    {
        MaxHedgedAttempts = maxHedgedAttempts;
        Delay = delay;
        HasDelayGenerator = hasDelayGenerator;
        HasNotification = hasNotification;
        HasActionGenerator = hasActionGenerator;
        HasHandlingOverride = hasHandlingOverride;
    }

    /// <summary>The maximum additional attempts after the primary.</summary>
    public int MaxHedgedAttempts { get; }

    /// <summary>The stagger delay between attempts.</summary>
    public TimeSpan Delay { get; }

    /// <summary>Whether a per-attempt delay generator is configured.</summary>
    public bool HasDelayGenerator { get; }

    /// <summary>Whether a hedge notification is configured.</summary>
    public bool HasNotification { get; }

    /// <summary>Whether an action generator is configured.</summary>
    public bool HasActionGenerator { get; }

    /// <summary>Whether local predicates replace the ambient handling clause.</summary>
    public bool HasHandlingOverride { get; }
}
