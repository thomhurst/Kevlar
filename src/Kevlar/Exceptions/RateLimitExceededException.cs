namespace Kevlar;

/// <summary>Thrown when a rate limit strategy rejects an execution.</summary>
public sealed class RateLimitExceededException : ExecutionRejectedException
{
    private const string DefaultMessage = "The rate limit has been exceeded.";

    /// <summary>Initializes a rate-limit rejection without a retry estimate.</summary>
    public RateLimitExceededException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Initializes a rate-limit rejection with a custom message.</summary>
    public RateLimitExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a rate-limit rejection with a custom message and cause.</summary>
    public RateLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes the exception.</summary>
    public RateLimitExceededException(TimeSpan? retryAfter)
        : base(DefaultMessage, retryAfter)
    {
    }
}
