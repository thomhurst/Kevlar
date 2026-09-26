namespace Kevlar.Testing;

/// <summary>Read-only circuit breaker configuration.</summary>
public sealed class CircuitBreakerStrategyDescriptor : StrategyDescriptor
{
    internal CircuitBreakerStrategyDescriptor(
        string description,
        int? consecutiveFailures,
        double? failureRatio,
        int minimumThroughput,
        TimeSpan samplingWindow,
        TimeSpan breakDuration,
        bool hasMonitor,
        bool hasNotification,
        bool hasHandlingOverride,
        int halfOpenProbes,
        TimeSpan? slowCallThreshold,
        double? slowCallRatio)
        : base(StrategyKind.CircuitBreaker, description)
    {
        ConsecutiveFailures = consecutiveFailures;
        FailureRatio = failureRatio;
        MinimumThroughput = minimumThroughput;
        SamplingWindow = samplingWindow;
        BreakDuration = breakDuration;
        HasMonitor = hasMonitor;
        HasNotification = hasNotification;
        HasHandlingOverride = hasHandlingOverride;
        HalfOpenProbes = halfOpenProbes;
        SlowCallThreshold = slowCallThreshold;
        SlowCallRatio = slowCallRatio;
    }

    /// <summary>The consecutive-failure threshold in simple mode.</summary>
    public int? ConsecutiveFailures { get; }

    /// <summary>The failure-ratio threshold in sampling mode.</summary>
    public double? FailureRatio { get; }

    /// <summary>Number of completed probes used to evaluate a half-open cohort.</summary>
    public int HalfOpenProbes { get; }

    /// <summary>Optional duration above which calls count as slow.</summary>
    public TimeSpan? SlowCallThreshold { get; }

    /// <summary>Optional slow-call ratio that trips the circuit.</summary>
    public double? SlowCallRatio { get; }

    /// <summary>Minimum sampled executions before ratio mode can trip.</summary>
    public int MinimumThroughput { get; }

    /// <summary>The rolling sampling window.</summary>
    public TimeSpan SamplingWindow { get; }

    /// <summary>The fixed open duration.</summary>
    public TimeSpan BreakDuration { get; }

    /// <summary>Whether a circuit monitor is configured.</summary>
    public bool HasMonitor { get; }

    /// <summary>Whether a state-change notification is configured.</summary>
    public bool HasNotification { get; }

    /// <summary>Whether local predicates replace the ambient handling clause.</summary>
    public bool HasHandlingOverride { get; }
}
