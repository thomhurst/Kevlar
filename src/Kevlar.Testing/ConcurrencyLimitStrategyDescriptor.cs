namespace Kevlar.Testing;

/// <summary>Read-only concurrency limiter configuration.</summary>
public sealed class ConcurrencyLimitStrategyDescriptor : StrategyDescriptor
{
    internal ConcurrencyLimitStrategyDescriptor(string description, int maxConcurrency, int maxQueue)
        : base(StrategyKind.ConcurrencyLimit, description)
    {
        MaxConcurrency = maxConcurrency;
        MaxQueue = maxQueue;
    }

    /// <summary>The maximum concurrent executions.</summary>
    public int MaxConcurrency { get; }

    /// <summary>The maximum wait queue size.</summary>
    public int MaxQueue { get; }
}
