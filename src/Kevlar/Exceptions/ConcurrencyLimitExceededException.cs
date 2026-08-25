namespace Kevlar;

/// <summary>Thrown when a concurrency limit strategy rejects an execution because both the concurrency and queue limits are full.</summary>
public sealed class ConcurrencyLimitExceededException : ExecutionRejectedException
{
    private const string DefaultMessage =
        "The concurrency limit's concurrency and queue limits are both full.";

    /// <summary>Initializes the exception.</summary>
    public ConcurrencyLimitExceededException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Initializes a concurrency-limit rejection with a custom message.</summary>
    public ConcurrencyLimitExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a concurrency-limit rejection with a custom message and cause.</summary>
    public ConcurrencyLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
