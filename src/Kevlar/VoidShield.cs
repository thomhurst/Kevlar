using Kevlar.Internal;

#pragma warning disable RS0026 // Execution overload parity intentionally keeps CancellationToken optional.

namespace Kevlar;

/// <summary>
/// An immutable, thread-safe resilience pipeline restricted to void executions because it contains
/// a void fallback. Obtained by chaining <c>Fallback(…)</c> from <see cref="Shield"/> or
/// <see cref="ShieldBuilder"/>. Subsequent fluent calls preserve the restriction.
/// </summary>
public sealed class VoidShield
{
    private readonly Shield _pipeline;

    internal VoidShield(Shield pipeline)
    {
        _pipeline = pipeline;
    }

    internal Shield Pipeline => _pipeline;

    internal Strategy[] Strategies => _pipeline.Strategies;

    internal TimeProvider TimeOrSystem => _pipeline.TimeOrSystem;

    /// <summary>The shield's diagnostic name, if assigned via <see cref="WithName"/>.</summary>
    public string? Name => _pipeline.Name;

    /// <summary>
    /// Gets whether every strategy guarantees invoking the execution continuation at most once.
    /// Custom strategies may opt in through <see cref="Strategy.InvokesContinuationAtMostOnce"/>.
    /// </summary>
    public bool InvokesContinuationAtMostOnce => _pipeline.InvokesContinuationAtMostOnce;

    /// <summary>Starts a handling clause for exceptions of type <typeparamref name="TException"/>.</summary>
    public VoidShieldBuilder When<TException>()
        where TException : Exception
        => new(_pipeline.When<TException>());

    /// <summary>Starts a handling clause for matching exceptions of type <typeparamref name="TException"/>.</summary>
    public VoidShieldBuilder When<TException>(Func<TException, bool> predicate)
        where TException : Exception
        => new(_pipeline.When(predicate));

    /// <summary>Starts a handling clause for exceptions matching <paramref name="predicate"/>.</summary>
    public VoidShieldBuilder When(Func<Exception, bool> predicate) => new(_pipeline.When(predicate));

    /// <summary>Resets subsequent reactive strategies to default exception handling.</summary>
    public VoidShield WhenAnyError() => new(_pipeline.WhenAnyError());

    /// <summary>Retries handled exceptions three times.</summary>
    public VoidShield Retry() => new(_pipeline.Retry());

    /// <summary>Retries handled exceptions up to <paramref name="maxRetries"/> times.</summary>
    public VoidShield Retry(int maxRetries) => new(_pipeline.Retry(maxRetries));

    /// <summary>Retries handled exceptions with the given backoff.</summary>
    public VoidShield Retry(int maxRetries, Backoff backoff) => new(_pipeline.Retry(maxRetries, backoff));

    /// <summary>Adds a configured retry strategy.</summary>
    public VoidShield Retry(Action<RetryOptions> configure) => new(_pipeline.Retry(configure));

    /// <summary>Retries handled exceptions indefinitely with default backoff.</summary>
    public VoidShield RetryForever() => new(_pipeline.RetryForever());

    /// <summary>Retries handled exceptions indefinitely with the given backoff.</summary>
    public VoidShield RetryForever(Backoff backoff) => new(_pipeline.RetryForever(backoff));

    /// <summary>Adds a timeout.</summary>
    public VoidShield Timeout(TimeSpan timeout) => new(_pipeline.Timeout(timeout));

    /// <summary>Adds a configured timeout.</summary>
    public VoidShield Timeout(Action<TimeoutOptions> configure) => new(_pipeline.Timeout(configure));

    /// <summary>Adds a circuit breaker.</summary>
    public VoidShield CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration) =>
        new(_pipeline.CircuitBreaker(consecutiveFailures, breakDuration));

    /// <summary>Adds a configured circuit breaker.</summary>
    public VoidShield CircuitBreaker(Action<CircuitBreakerOptions> configure) =>
        new(_pipeline.CircuitBreaker(configure));

    /// <summary>Adds a token-bucket rate limit.</summary>
    public VoidShield RateLimit(int permits, TimeSpan perWindow) =>
        new(_pipeline.RateLimit(permits, perWindow));

    /// <summary>Adds a configured rate limit.</summary>
    public VoidShield RateLimit(Action<RateLimitOptions> configure) => new(_pipeline.RateLimit(configure));

    /// <summary>Adds a concurrency limit.</summary>
    public VoidShield ConcurrencyLimit(int maxConcurrency, int maxQueue = 0) =>
        new(_pipeline.ConcurrencyLimit(maxConcurrency, maxQueue));

    /// <summary>Adds a configured concurrency limit.</summary>
    public VoidShield ConcurrencyLimit(Action<ConcurrencyLimitOptions> configure) =>
        new(_pipeline.ConcurrencyLimit(configure));

    /// <summary>Adds hedging for an idempotent void action.</summary>
    public VoidShield Hedge(int maxAttempts, TimeSpan delay) => new(_pipeline.Hedge(maxAttempts, delay));

    /// <summary>Adds configured hedging for an idempotent void action.</summary>
    public VoidShield Hedge(Action<HedgeOptions> configure) => new(_pipeline.Hedge(configure));

    /// <summary>Appends a custom strategy.</summary>
    public VoidShield Use(Strategy strategy) => new(_pipeline.Use(strategy));

    /// <summary>Appends a custom strategy created from the active handling clause.</summary>
    public VoidShield Use(Func<HandlingClause, Strategy> factory) => new(_pipeline.Use(factory));

    /// <summary>Runs <paramref name="fallback"/> in place of handled failures.</summary>
    public VoidShield Fallback(Func<Exception, CancellationToken, ValueTask> fallback) =>
        _pipeline.Fallback(fallback);

    /// <summary>Runs <paramref name="fallback"/> in place of handled failures and configures notifications.</summary>
    public VoidShield Fallback(
        Func<Exception, CancellationToken, ValueTask> fallback,
        Action<FallbackOptions> configure) =>
        _pipeline.Fallback(fallback, configure);

    /// <summary>Runs <paramref name="fallback"/> in place of handled failures.</summary>
    public VoidShield Fallback(Func<CancellationToken, ValueTask> fallback) => _pipeline.Fallback(fallback);

    /// <summary>Runs <paramref name="fallback"/> in place of handled failures and configures notifications.</summary>
    public VoidShield Fallback(
        Func<CancellationToken, ValueTask> fallback,
        Action<FallbackOptions> configure) =>
        _pipeline.Fallback(fallback, configure);

    /// <summary>Wraps a result-polymorphic shield inside this void-only shield.</summary>
    public VoidShield Wrap(Shield inner)
    {
        Throw.IfNull(inner, nameof(inner));
        return new VoidShield(_pipeline.Wrap(inner));
    }

    /// <summary>Wraps another void-only shield inside this shield.</summary>
    public VoidShield Wrap(VoidShield inner)
    {
        Throw.IfNull(inner, nameof(inner));
        return new VoidShield(_pipeline.Wrap(inner._pipeline));
    }

    /// <summary>Returns a copy with a diagnostic name.</summary>
    public VoidShield WithName(string name) => new(_pipeline.WithName(name));

    /// <summary>Returns a copy using the given time provider.</summary>
    public VoidShield WithTimeProvider(TimeProvider timeProvider) => new(_pipeline.WithTimeProvider(timeProvider));

    /// <summary>Executes a void delegate through the pipeline.</summary>
    public ValueTask ExecuteAsync(
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken = default) =>
        _pipeline.ExecuteAsync(action, cancellationToken);

    /// <summary>Executes a stateful void delegate through the pipeline.</summary>
    public ValueTask ExecuteAsync<TState>(
        TState state,
        Func<TState, CancellationToken, ValueTask> action,
        CancellationToken cancellationToken = default) =>
        _pipeline.ExecuteAsync(state, action, cancellationToken);

    /// <summary>Initializes properties, then executes a context-aware void delegate.</summary>
    public ValueTask ExecuteWithContextAsync<TState>(
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, ValueTask> action,
        CancellationToken cancellationToken = default) =>
        _pipeline.ExecuteWithContextAsync(state, initializeProperties, action, cancellationToken);

    /// <summary>Executes a context-aware void delegate without seeding properties.</summary>
    public ValueTask ExecuteWithContextAsync(
        Func<KevlarContext, ValueTask> action,
        CancellationToken cancellationToken = default) =>
        _pipeline.ExecuteWithContextAsync(action, cancellationToken);

    /// <summary>Executes a synchronous void delegate through the pipeline.</summary>
    public void Execute(Action<CancellationToken> action, CancellationToken cancellationToken = default) =>
        _pipeline.Execute(action, cancellationToken);

    /// <summary>Executes a synchronous stateful void delegate through the pipeline.</summary>
    public void Execute<TState>(
        TState state,
        Action<TState, CancellationToken> action,
        CancellationToken cancellationToken = default) =>
        _pipeline.Execute(state, action, cancellationToken);

    /// <summary>Initializes properties, then executes a synchronous context-aware void delegate.</summary>
    public void ExecuteWithContext<TState>(
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Action<TState, KevlarContext> action,
        CancellationToken cancellationToken = default) =>
        _pipeline.ExecuteWithContext(state, initializeProperties, action, cancellationToken);

    /// <summary>Executes a synchronous context-aware void delegate without seeding properties.</summary>
    public void ExecuteWithContext(
        Action<KevlarContext> action,
        CancellationToken cancellationToken = default) =>
        _pipeline.ExecuteWithContext(action, cancellationToken);

    /// <summary>Describes the pipeline, outermost strategy first.</summary>
    public override string ToString() => _pipeline.ToString();
}

#pragma warning restore RS0026
