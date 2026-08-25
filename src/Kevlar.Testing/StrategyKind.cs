namespace Kevlar.Testing;

/// <summary>Stable category for a strategy in a shield descriptor.</summary>
public enum StrategyKind
{
    /// <summary>A caller-defined strategy.</summary>
    Custom,

    /// <summary>A retry strategy.</summary>
    Retry,

    /// <summary>A timeout strategy.</summary>
    Timeout,

    /// <summary>A circuit breaker strategy.</summary>
    CircuitBreaker,

    /// <summary>A rate limiter strategy.</summary>
    RateLimit,

    /// <summary>A concurrency limiter strategy.</summary>
    ConcurrencyLimit,

    /// <summary>A hedging strategy.</summary>
    Hedge,

    /// <summary>A fallback strategy.</summary>
    Fallback,

    /// <summary>A strategy backed by System.Threading.RateLimiting.</summary>
    RateLimiterAdapter,
}
