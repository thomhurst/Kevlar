namespace Kevlar;

/// <summary>Configures a concurrency limit that responds to downstream latency and failures.</summary>
/// <remarks>
/// Completed calls are sampled using the shield's <see cref="TimeProvider"/>. Excess callers are
/// rejected immediately; this strategy has no wait queue. Caller cancellation is excluded from
/// samples. Place a timeout inside this strategy to observe its timeout outcome as a failure.
/// </remarks>
public sealed class AdaptiveConcurrencyLimitOptions
{
    /// <summary>An optional low-cardinality name used by strategy telemetry.</summary>
    public string? Name { get; set; }

    /// <summary>The adjustment algorithm. Default <see cref="AdaptiveConcurrencyLimitAlgorithm.Aimd"/>.</summary>
    public AdaptiveConcurrencyLimitAlgorithm Algorithm { get; set; }

    /// <summary>The minimum limit, which must be positive. Default 1.</summary>
    public int MinLimit { get; set; } = 1;

    /// <summary>The maximum limit, which must be at least <see cref="MinLimit"/>. Default 100.</summary>
    public int MaxLimit { get; set; } = 100;

    /// <summary>The starting limit, between <see cref="MinLimit"/> and <see cref="MaxLimit"/>. Default 10.</summary>
    public int InitialLimit { get; set; } = 10;

    /// <summary>The minimum interval between adjustments, evaluated on completion. Default one second.</summary>
    public TimeSpan SamplingWindow { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The multiplier applied to a congested limit, greater than zero and less than one. Default 0.9.</summary>
    public double DecreaseFactor { get; set; } = 0.9;

    /// <summary>The successful mean latency multiplier above which a window is congested. Default 2.</summary>
    /// <remarks>
    /// Must be at least one. The reference latency starts with the first successful window and then
    /// uses a moving average with 10% weight for each subsequent successful window.
    /// </remarks>
    public double LatencyTolerance { get; set; } = 2;

    /// <summary>Invoked and awaited when admission is rejected. Callback failures do not replace the rejection.</summary>
    public Func<ConcurrencyLimitRejectedEvent, ValueTask>? OnRejected { get; set; }
}
