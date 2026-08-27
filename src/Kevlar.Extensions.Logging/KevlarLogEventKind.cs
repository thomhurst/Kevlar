namespace Kevlar.Extensions.Logging;

/// <summary>Identifies the stable logging event emitted for a Kevlar strategy event.</summary>
public enum KevlarLogEventKind
{
    /// <summary>No logging event kind.</summary>
    None = 0,

    /// <summary>A retry is about to run.</summary>
    Retry = 1,

    /// <summary>An execution exceeded a timeout.</summary>
    Timeout = 2,

    /// <summary>A circuit breaker changed state or rejected an execution.</summary>
    CircuitStateChanged = 3,

    /// <summary>A hedged attempt started.</summary>
    Hedge = 4,

    /// <summary>A fallback replaced an outcome.</summary>
    Fallback = 5,

    /// <summary>A rate limiter rejected an execution.</summary>
    RateLimitRejected = 6,

    /// <summary>A concurrency limiter rejected an execution.</summary>
    ConcurrencyLimitRejected = 7,

    /// <summary>A user callback threw.</summary>
    CallbackError = 8,

    /// <summary>An open or isolated circuit rejected an execution.</summary>
    CircuitRejected = 9,

    /// <summary>HTTP replay safety disabled configured additional attempts.</summary>
    AttemptsSuppressed = 10,

    /// <summary>An execution completed after ignoring timeout cancellation.</summary>
    TimeoutIgnored = 11,

    /// <summary>A hedged execution attempt completed.</summary>
    HedgeAttempt = 12,
}
