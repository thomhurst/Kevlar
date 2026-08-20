namespace Kevlar;

/// <summary>Thrown when a rate limit strategy rejects an execution.</summary>
public sealed class RateLimitExceededException : KevlarException
{
    /// <summary>Initializes the exception.</summary>
    public RateLimitExceededException(TimeSpan? retryAfter)
        : base("The rate limit has been exceeded.")
        => RetryAfter = retryAfter;

    /// <summary>Estimated time until a permit becomes available, when known.</summary>
    public TimeSpan? RetryAfter { get; }
}
