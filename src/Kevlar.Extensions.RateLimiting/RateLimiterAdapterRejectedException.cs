namespace Kevlar.Extensions.RateLimiting;

/// <summary>Thrown when a System.Threading.RateLimiting adapter rejects an execution.</summary>
public sealed class RateLimiterAdapterRejectedException : ExecutionRejectedException
{
    private const string DefaultMessage = "The adapted rate limiter rejected the execution.";

    /// <summary>Initializes an adapter rejection without a retry estimate.</summary>
    public RateLimiterAdapterRejectedException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Initializes an adapter rejection with a custom message.</summary>
    public RateLimiterAdapterRejectedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes an adapter rejection with a custom message and cause.</summary>
    public RateLimiterAdapterRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes an adapter rejection with an optional retry estimate.</summary>
    public RateLimiterAdapterRejectedException(TimeSpan? retryAfter)
        : base(DefaultMessage, retryAfter)
    {
    }
}
