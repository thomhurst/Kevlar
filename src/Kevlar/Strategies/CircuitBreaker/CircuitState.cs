namespace Kevlar;

/// <summary>The state of a circuit breaker.</summary>
public enum CircuitState
{
    /// <summary>Executions flow normally; failures are being measured.</summary>
    Closed,

    /// <summary>Executions are rejected until the break duration elapses.</summary>
    Open,

    /// <summary>A single probe execution is allowed to test recovery.</summary>
    HalfOpen,

    /// <summary>Manually forced open; executions are rejected until <see cref="CircuitBreakerMonitor.Reset"/>.</summary>
    Isolated,
}

/// <summary>Describes a circuit breaker state transition.</summary>
public readonly struct CircuitStateChangedEvent
{
    internal CircuitStateChangedEvent(CircuitState from, CircuitState to, Exception? lastException)
    {
        From = from;
        To = to;
        LastException = lastException;
    }

    /// <summary>The state the circuit left.</summary>
    public CircuitState From { get; }

    /// <summary>The state the circuit entered.</summary>
    public CircuitState To { get; }

    /// <summary>The exception from the failure that caused the transition, when applicable.</summary>
    public Exception? LastException { get; }
}
