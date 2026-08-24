namespace Kevlar;

/// <summary>
/// An immutable exception-handling clause under construction for a <see cref="VoidShield"/> chain.
/// Every strategy that finishes the clause returns another <see cref="VoidShield"/>.
/// </summary>
public sealed class VoidShieldBuilder
{
    private readonly ShieldBuilder _builder;

    internal VoidShieldBuilder(ShieldBuilder builder)
    {
        _builder = builder;
    }

    /// <summary>Returns a new builder that also handles exceptions of type <typeparamref name="TException"/>.</summary>
    public VoidShieldBuilder Or<TException>()
        where TException : Exception
        => new(_builder.Or<TException>());

    /// <summary>Returns a new builder that also handles matching exceptions of type <typeparamref name="TException"/>.</summary>
    public VoidShieldBuilder Or<TException>(Func<TException, bool> predicate)
        where TException : Exception
        => new(_builder.Or(predicate));

    /// <summary>Returns a new builder that also handles exceptions matching <paramref name="predicate"/>.</summary>
    public VoidShieldBuilder Or(Func<Exception, bool> predicate) => new(_builder.Or(predicate));

    /// <summary>Retries handled exceptions three times.</summary>
    public VoidShield Retry() => new(_builder.Retry());

    /// <summary>Retries handled exceptions.</summary>
    public VoidShield Retry(int maxRetries) => new(_builder.Retry(maxRetries));

    /// <summary>Retries handled exceptions with the given backoff.</summary>
    public VoidShield Retry(int maxRetries, Backoff backoff) => new(_builder.Retry(maxRetries, backoff));

    /// <summary>Adds a configured retry.</summary>
    public VoidShield Retry(Action<RetryOptions> configure) => new(_builder.Retry(configure));

    /// <summary>Retries handled exceptions indefinitely.</summary>
    public VoidShield RetryForever() => new(_builder.RetryForever());

    /// <summary>Retries handled exceptions indefinitely with the given backoff.</summary>
    public VoidShield RetryForever(Backoff backoff) => new(_builder.RetryForever(backoff));

    /// <summary>Adds a circuit breaker.</summary>
    public VoidShield CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration) =>
        new(_builder.CircuitBreaker(consecutiveFailures, breakDuration));

    /// <summary>Adds a configured circuit breaker.</summary>
    public VoidShield CircuitBreaker(Action<CircuitBreakerOptions> configure) =>
        new(_builder.CircuitBreaker(configure));

    /// <summary>Adds hedging for an idempotent void action.</summary>
    public VoidShield Hedge(int maxAttempts, TimeSpan delay) => new(_builder.Hedge(maxAttempts, delay));

    /// <summary>Adds configured hedging for an idempotent void action.</summary>
    public VoidShield Hedge(Action<HedgeOptions> configure) => new(_builder.Hedge(configure));

    /// <summary>Appends a custom strategy created from the accumulated handling clause.</summary>
    public VoidShield Use(Func<HandlingClause, Strategy> factory) => new(_builder.Use(factory));

    /// <summary>Runs <paramref name="fallback"/> in place of handled failures.</summary>
    public VoidShield Fallback(Func<Exception, CancellationToken, ValueTask> fallback) =>
        _builder.Fallback(fallback);

    /// <summary>Runs <paramref name="fallback"/> in place of handled failures and configures notifications.</summary>
    public VoidShield Fallback(
        Func<Exception, CancellationToken, ValueTask> fallback,
        Action<FallbackOptions> configure) =>
        _builder.Fallback(fallback, configure);

    /// <summary>Adds a timeout while preserving the clause for later reactive strategies.</summary>
    public VoidShield Timeout(TimeSpan timeout) => new(_builder.Timeout(timeout));

    /// <summary>Adds a rate limit while preserving the clause.</summary>
    public VoidShield RateLimit(int permits, TimeSpan perWindow) => new(_builder.RateLimit(permits, perWindow));

    /// <summary>Adds a configured rate limit while preserving the clause.</summary>
    public VoidShield RateLimit(Action<RateLimitOptions> configure) => new(_builder.RateLimit(configure));

    /// <summary>Adds a concurrency limit while preserving the clause.</summary>
    public VoidShield ConcurrencyLimit(int maxConcurrency, int maxQueue = 0) =>
        new(_builder.ConcurrencyLimit(maxConcurrency, maxQueue));

    /// <summary>Adds a configured concurrency limit while preserving the clause.</summary>
    public VoidShield ConcurrencyLimit(Action<ConcurrencyLimitOptions> configure) =>
        new(_builder.ConcurrencyLimit(configure));
}
