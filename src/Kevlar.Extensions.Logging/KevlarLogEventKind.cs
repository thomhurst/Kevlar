namespace Kevlar.Extensions.Logging;

/// <summary>Identifies the stable logging event emitted for a Kevlar strategy event.</summary>
public enum KevlarLogEventKind
{
    /// <summary>A retry is about to run.</summary>
    Retry,

    /// <summary>An execution exceeded a timeout.</summary>
    Timeout,

    /// <summary>A circuit breaker changed state or rejected an execution.</summary>
    CircuitState,

    /// <summary>A hedged attempt started.</summary>
    Hedge,

    /// <summary>A fallback replaced an outcome.</summary>
    Fallback,

    /// <summary>A rate limiter rejected an execution.</summary>
    RateLimitRejected,

    /// <summary>A concurrency limiter rejected an execution.</summary>
    ConcurrencyLimitRejected,

    /// <summary>A user callback threw.</summary>
    CallbackError,

    /// <summary>An open or isolated circuit rejected an execution.</summary>
    CircuitRejected,

    /// <summary>HTTP replay safety disabled configured additional attempts.</summary>
    AttemptsSuppressed,
}
