namespace Kevlar;

/// <summary>Thrown when a circuit breaker rejects an execution because the circuit is open.</summary>
public sealed class CircuitOpenException : ExecutionRejectedException
{
    private const string OpenMessage = "The circuit is open and is rejecting executions.";
    private const string IsolatedMessage =
        "The circuit is manually isolated and is rejecting executions until it is reset.";

    /// <summary>Initializes an open-circuit rejection.</summary>
    public CircuitOpenException()
        : base(OpenMessage)
    {
    }

    /// <summary>Initializes an open-circuit rejection with a custom message.</summary>
    public CircuitOpenException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes an open-circuit rejection with a custom message and last failure.</summary>
    public CircuitOpenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes the exception.</summary>
    public CircuitOpenException(TimeSpan? retryAfter, bool isIsolated, Exception? lastException)
        : base(isIsolated ? IsolatedMessage : OpenMessage, retryAfter, lastException)
    {
        IsIsolated = isIsolated;
    }

    /// <summary><see langword="true"/> when the circuit was manually isolated rather than tripped by failures.</summary>
    public bool IsIsolated { get; }
}
