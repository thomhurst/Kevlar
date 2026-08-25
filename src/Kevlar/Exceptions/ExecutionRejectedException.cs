namespace Kevlar;

/// <summary>Base class for executions rejected by a Kevlar strategy.</summary>
public abstract class ExecutionRejectedException : KevlarException
{
    /// <summary>Initializes a rejection with no retry estimate.</summary>
    protected ExecutionRejectedException()
    {
    }

    /// <summary>Initializes a rejection with no retry estimate.</summary>
    protected ExecutionRejectedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a rejection with no retry estimate and a cause.</summary>
    protected ExecutionRejectedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a rejection with an optional retry estimate and cause.</summary>
    protected ExecutionRejectedException(
        string message,
        TimeSpan? retryAfter,
        Exception? innerException = null)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>Estimated time until execution may be attempted again, when known.</summary>
    public TimeSpan? RetryAfter { get; }
}
