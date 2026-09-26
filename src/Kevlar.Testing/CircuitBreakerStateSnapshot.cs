namespace Kevlar.Testing;

/// <summary>An immutable circuit-breaker state snapshot.</summary>
public sealed class CircuitBreakerStateSnapshot : StrategyStateSnapshot
{
    internal CircuitBreakerStateSnapshot(int strategyIndex, CircuitState state, int probesInFlight, long slowCallCount)
        : base(StrategyKind.CircuitBreaker, strategyIndex)
    {
        State = state;
        ProbesInFlight = probesInFlight;
        SlowCallCount = slowCallCount;
    }

    /// <summary>Gets the circuit state.</summary>
    public CircuitState State { get; }

    /// <summary>Gets the number of in-flight probes in the current half-open cohort.</summary>
    public int ProbesInFlight { get; }

    /// <summary>Gets the slow-call count in the current closed-state sampling window.</summary>
    /// <remarks>Closing or resetting the circuit clears the window. Half-open probes are evaluated separately.</remarks>
    public long SlowCallCount { get; }
}
