namespace Kevlar;

/// <summary>Bounded identity of the strategy associated with a structured event.</summary>
public enum KevlarStrategyKind
{
    /// <summary>The event belongs to the public execution rather than a strategy.</summary>
    None,

    /// <summary>A retry strategy.</summary>
    Retry,

    /// <summary>A timeout strategy.</summary>
    Timeout,

    /// <summary>A hedging strategy.</summary>
    Hedging,

    /// <summary>A fallback strategy.</summary>
    Fallback,

    /// <summary>A circuit-breaker strategy.</summary>
    CircuitBreaker,

    /// <summary>A rate-limit strategy.</summary>
    RateLimit,

    /// <summary>A concurrency-limit strategy.</summary>
    ConcurrencyLimit,
}
