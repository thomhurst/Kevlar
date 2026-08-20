namespace Kevlar;

/// <summary>Thrown when a circuit breaker rejects an execution because the circuit is open.</summary>
public sealed class CircuitOpenException : KevlarException
{
    /// <summary>Initializes the exception.</summary>
    public CircuitOpenException(TimeSpan? retryAfter, bool isIsolated, Exception? lastException)
        : base(isIsolated
            ? "The circuit is manually isolated and is rejecting executions until it is reset."
            : "The circuit is open and is rejecting executions.", lastException!)
    {
        RetryAfter = retryAfter;
        IsIsolated = isIsolated;
    }

    /// <summary>Time remaining until the circuit will allow a probe execution, when known.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary><see langword="true"/> when the circuit was manually isolated rather than tripped by failures.</summary>
    public bool IsIsolated { get; }
}
