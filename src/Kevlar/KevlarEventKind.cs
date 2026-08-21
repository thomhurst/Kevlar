namespace Kevlar;

/// <summary>Identifies a structured event emitted by Kevlar.</summary>
public enum KevlarEventKind
{
    /// <summary>A public shield execution is starting.</summary>
    ExecutionStarted,

    /// <summary>A public shield execution completed.</summary>
    ExecutionCompleted,

    /// <summary>A retry was scheduled.</summary>
    RetryScheduled,

    /// <summary>A timeout elapsed.</summary>
    Timeout,

    /// <summary>An additional hedge attempt started.</summary>
    HedgeStarted,

    /// <summary>A fallback replaced a handled outcome.</summary>
    Fallback,

    /// <summary>A circuit breaker changed state.</summary>
    CircuitStateChanged,

    /// <summary>A rate limiter rejected an execution.</summary>
    RateLimitRejected,

    /// <summary>A concurrency limiter rejected an execution.</summary>
    ConcurrencyLimitRejected,
}
