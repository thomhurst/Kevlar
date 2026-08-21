namespace Kevlar.Testing;

/// <summary>An immutable circuit-breaker state snapshot.</summary>
public sealed class CircuitBreakerStateSnapshot : StrategyStateSnapshot
{
    internal CircuitBreakerStateSnapshot(int strategyIndex, CircuitState state)
        : base(StrategyKind.CircuitBreaker, strategyIndex)
    {
        State = state;
    }

    /// <summary>Gets the circuit state.</summary>
    public CircuitState State { get; }
}
