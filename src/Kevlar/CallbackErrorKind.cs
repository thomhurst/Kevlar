namespace Kevlar;

/// <summary>Identifies a strategy notification whose failure was reported by Kevlar.</summary>
public enum CallbackErrorKind
{
    /// <summary>A retry notification.</summary>
    Retry,

    /// <summary>A timeout notification.</summary>
    Timeout,

    /// <summary>A circuit-breaker state-change notification.</summary>
    CircuitStateChanged,

    /// <summary>A circuit-breaker monitor subscriber.</summary>
    CircuitMonitor,

    /// <summary>A hedge notification.</summary>
    Hedge,

    /// <summary>A fallback notification.</summary>
    Fallback,

    /// <summary>A concurrency-limit rejection notification.</summary>
    ConcurrencyLimitRejected,

    /// <summary>A built-in rate-limit rejection notification.</summary>
    RateLimitRejected,

    /// <summary>A System.Threading.RateLimiting adapter rejection notification.</summary>
    RateLimiterAdapterRejected,

    /// <summary>A chaos-injection notification.</summary>
    ChaosInjected,
}
