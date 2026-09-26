namespace Kevlar.Testing;

/// <summary>Read-only configuration of an adaptive concurrency limiter.</summary>
public sealed class AdaptiveConcurrencyLimitStrategyDescriptor : StrategyDescriptor
{
    internal AdaptiveConcurrencyLimitStrategyDescriptor(
        string description, int minLimit, int maxLimit, int initialLimit, TimeSpan samplingWindow,
        double decreaseFactor, double latencyTolerance, bool hasNotification)
        : base(StrategyKind.ConcurrencyLimit, description)
    {
        MinLimit = minLimit;
        MaxLimit = maxLimit;
        InitialLimit = initialLimit;
        SamplingWindow = samplingWindow;
        DecreaseFactor = decreaseFactor;
        LatencyTolerance = latencyTolerance;
        HasNotification = hasNotification;
    }

    /// <summary>The algorithm used to adjust the limit.</summary>
    public AdaptiveConcurrencyLimitAlgorithm Algorithm => AdaptiveConcurrencyLimitAlgorithm.Aimd;

    /// <summary>The minimum admission limit.</summary>
    public int MinLimit { get; }

    /// <summary>The maximum admission limit.</summary>
    public int MaxLimit { get; }

    /// <summary>The initial admission limit.</summary>
    public int InitialLimit { get; }

    /// <summary>The minimum interval between adjustments.</summary>
    public TimeSpan SamplingWindow { get; }

    /// <summary>The limit multiplier applied after congestion.</summary>
    public double DecreaseFactor { get; }

    /// <summary>The allowed multiplier above the moving latency baseline.</summary>
    public double LatencyTolerance { get; }

    /// <summary>Whether rejection notifications are configured.</summary>
    public bool HasNotification { get; }
}
