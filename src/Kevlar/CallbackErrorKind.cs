namespace Kevlar;

/// <summary>Identifies an isolated callback or cleanup failure reported by Kevlar.</summary>
public enum CallbackErrorKind
{
    /// <summary>No callback kind.</summary>
    None = 0,

    /// <summary>A retry notification.</summary>
    Retry = 1,

    /// <summary>A timeout notification.</summary>
    Timeout = 2,

    /// <summary>A circuit-breaker state-change notification.</summary>
    CircuitStateChanged = 3,

    /// <summary>A circuit-breaker monitor subscriber.</summary>
    CircuitMonitor = 4,

    /// <summary>A hedge notification.</summary>
    Hedge = 5,

    /// <summary>A fallback notification.</summary>
    Fallback = 6,

    /// <summary>A concurrency-limit rejection notification.</summary>
    ConcurrencyLimitRejected = 7,

    /// <summary>A built-in rate-limit rejection notification.</summary>
    RateLimitRejected = 8,

    /// <summary>A callback owned by a custom strategy or integration.</summary>
    Custom = 9,

    /// <summary>Disposal of a superseded result.</summary>
    ResultDisposal = 10,

    /// <summary>A handling-clause or strategy handling predicate.</summary>
    HandlingPredicate = 11,
}
