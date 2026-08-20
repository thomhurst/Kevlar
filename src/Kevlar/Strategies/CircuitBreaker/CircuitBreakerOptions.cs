namespace Kevlar;

/// <summary>
/// Configuration for a circuit breaker strategy. Configure either
/// <see cref="ConsecutiveFailures"/> (simple mode) or <see cref="FailureRatio"/>
/// (sampling mode). When neither is set, the breaker trips after 5 consecutive failures.
/// </summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>Trips the circuit after this many consecutive handled failures.</summary>
    public int? ConsecutiveFailures { get; set; }

    /// <summary>
    /// Trips the circuit when the ratio of handled failures within <see cref="SamplingWindow"/>
    /// reaches this value (0 to 1), provided at least <see cref="MinimumThroughput"/> executions
    /// were observed in the window.
    /// </summary>
    public double? FailureRatio { get; set; }

    /// <summary>Minimum executions in the sampling window before <see cref="FailureRatio"/> can trip the circuit. Default 10.</summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>The rolling window over which the failure ratio is measured. Default 30 seconds.</summary>
    public TimeSpan SamplingWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the circuit stays open before allowing a probe. Default 15 seconds.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// An optional monitor giving external code visibility of the circuit state plus manual
    /// <see cref="CircuitBreakerMonitor.Isolate"/> / <see cref="CircuitBreakerMonitor.Reset"/> control.
    /// A monitor can be bound to only one circuit breaker.
    /// </summary>
    public CircuitBreakerMonitor? Monitor { get; set; }

    /// <summary>Invoked on every state transition.</summary>
    public Action<CircuitStateChangedEvent>? OnStateChanged { get; set; }
}
