using Kevlar.Strategies;

namespace Kevlar;

/// <summary>
/// Observes and manually controls a single circuit breaker. Create one, assign it to
/// <see cref="CircuitBreakerOptions.Monitor"/>, and keep a reference to it.
/// </summary>
public sealed class CircuitBreakerMonitor
{
    private CircuitBreakerCore? _core;

    /// <summary>The current state of the bound circuit.</summary>
    public CircuitState State => BoundCore().State;

    /// <summary>Raised on every state transition of the bound circuit.</summary>
    public event Action<CircuitStateChangedEvent>? StateChanged;

    /// <summary>Forces the circuit open. Executions are rejected until <see cref="Reset"/> is called.</summary>
    public void Isolate() => BoundCore().Isolate();

    /// <summary>Closes the circuit and clears all failure metrics.</summary>
    public void Reset() => BoundCore().Reset();

    internal void Bind(CircuitBreakerCore core)
    {
        if (Interlocked.CompareExchange(ref _core, core, null) is not null)
        {
            throw new InvalidOperationException("This CircuitBreakerMonitor is already bound to another circuit breaker.");
        }
    }

    internal void Raise(in CircuitStateChangedEvent stateChangedEvent) => StateChanged?.Invoke(stateChangedEvent);

    private CircuitBreakerCore BoundCore() =>
        _core ?? throw new InvalidOperationException(
            "This CircuitBreakerMonitor has not been bound. Assign it to CircuitBreakerOptions.Monitor when building the policy.");
}
