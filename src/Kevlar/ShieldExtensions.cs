using Kevlar.Internal;
using Kevlar.Strategies;

namespace Kevlar;

/// <summary>
/// Fluent chaining for <see cref="Shield"/>. Each call returns a new immutable shield with the
/// strategy appended; the earliest strategy in a chain is the outermost at execution time.
/// </summary>
public static class ShieldExtensions
{
    /// <summary>Appends a retry of handled exceptions, up to <paramref name="maxRetries"/> times, with the default exponential jittered backoff.</summary>
    public static Shield Retry(this Shield shield, int maxRetries = 3) => shield.Retry(options => options.MaxRetries = maxRetries);

    /// <summary>Appends a retry of handled exceptions, up to <paramref name="maxRetries"/> times, with the given backoff.</summary>
    public static Shield Retry(this Shield shield, int maxRetries, Backoff backoff) => shield.Retry(options =>
    {
        options.MaxRetries = maxRetries;
        options.Backoff = backoff;
    });

    /// <summary>Appends a retry strategy configured via <paramref name="configure"/>.</summary>
    public static Shield Retry(this Shield shield, Action<RetryOptions> configure)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(configure, nameof(configure));
        var options = new RetryOptions();
        configure(options);
        return shield.Append(new RetryStrategy(options, shield.JudgeOrDefault));
    }

    /// <summary>Appends a retry that never gives up.</summary>
    public static Shield RetryForever(this Shield shield, Backoff? backoff = null) => shield.Retry(options =>
    {
        options.MaxRetries = int.MaxValue;
        options.Backoff = backoff ?? Backoff.Default;
    });

    /// <summary>Appends a timeout, surfacing <see cref="TimeoutExceededException"/> when exceeded.</summary>
    public static Shield Timeout(this Shield shield, TimeSpan timeout) => shield.Timeout(options => options.Timeout = timeout);

    /// <summary>Appends a timeout strategy configured via <paramref name="configure"/>.</summary>
    public static Shield Timeout(this Shield shield, Action<TimeoutOptions> configure)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(configure, nameof(configure));
        var options = new TimeoutOptions();
        configure(options);
        return shield.Append(new TimeoutStrategy(options));
    }

    /// <summary>Appends a circuit breaker that opens for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive handled exceptions.</summary>
    public static Shield CircuitBreaker(this Shield shield, int consecutiveFailures, TimeSpan breakDuration) => shield.CircuitBreaker(options =>
    {
        options.ConsecutiveFailures = consecutiveFailures;
        options.BreakDuration = breakDuration;
    });

    /// <summary>Appends a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public static Shield CircuitBreaker(this Shield shield, Action<CircuitBreakerOptions> configure)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(configure, nameof(configure));
        var options = new CircuitBreakerOptions();
        configure(options);
        return shield.Append(new CircuitBreakerStrategy(options, shield.JudgeOrDefault));
    }

    /// <summary>Appends a token-bucket rate limit of <paramref name="permits"/> executions per <paramref name="perWindow"/>.</summary>
    public static Shield RateLimit(this Shield shield, int permits, TimeSpan perWindow) => shield.RateLimit(options =>
    {
        options.Permits = permits;
        options.Window = perWindow;
    });

    /// <summary>Appends a rate limit strategy configured via <paramref name="configure"/>.</summary>
    public static Shield RateLimit(this Shield shield, Action<RateLimitOptions> configure)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(configure, nameof(configure));
        var options = new RateLimitOptions();
        configure(options);
        return shield.Append(new RateLimitStrategy(options));
    }

    /// <summary>Appends a concurrency limit capping concurrency at <paramref name="maxConcurrency"/> with an optional wait queue.</summary>
    public static Shield ConcurrencyLimit(this Shield shield, int maxConcurrency, int maxQueue = 0) => shield.ConcurrencyLimit(options =>
    {
        options.MaxConcurrency = maxConcurrency;
        options.MaxQueue = maxQueue;
    });

    /// <summary>Appends a concurrency limit strategy configured via <paramref name="configure"/>.</summary>
    public static Shield ConcurrencyLimit(this Shield shield, Action<ConcurrencyLimitOptions> configure)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(configure, nameof(configure));
        var options = new ConcurrencyLimitOptions();
        configure(options);
        return shield.Append(new ConcurrencyLimitStrategy(options));
    }

    /// <summary>Appends hedging: up to <paramref name="maxAttempts"/> concurrent attempts staggered by <paramref name="delay"/>.</summary>
    public static Shield Hedge(this Shield shield, int maxAttempts, TimeSpan delay) => shield.Hedge(options =>
    {
        options.MaxAttempts = maxAttempts;
        options.Delay = delay;
    });

    /// <summary>Appends a hedging strategy configured via <paramref name="configure"/>.</summary>
    public static Shield Hedge(this Shield shield, Action<HedgingOptions> configure)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(configure, nameof(configure));
        var options = new HedgingOptions();
        configure(options);
        return shield.Append(new HedgingStrategy(options, shield.JudgeOrDefault));
    }

    /// <summary>Starts a handling clause: subsequent reactive strategies act on exceptions of type <typeparamref name="TException"/>.</summary>
    public static ShieldBuilder When<TException>(this Shield shield)
        where TException : Exception
    {
        Throw.IfNull(shield, nameof(shield));
        return new ShieldBuilder(shield).Or<TException>();
    }

    /// <summary>Starts a handling clause for exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>.</summary>
    public static ShieldBuilder When<TException>(this Shield shield, Func<TException, bool> predicate)
        where TException : Exception
    {
        Throw.IfNull(shield, nameof(shield));
        return new ShieldBuilder(shield).Or(predicate);
    }

    /// <summary>Starts a handling clause for exceptions matching <paramref name="predicate"/>.</summary>
    public static ShieldBuilder When(this Shield shield, Func<Exception, bool> predicate)
    {
        Throw.IfNull(shield, nameof(shield));
        return new ShieldBuilder(shield).OrWhen(predicate);
    }

    /// <summary>Appends a custom <see cref="Strategy"/> implementation to the pipeline.</summary>
    public static Shield Use(this Shield shield, Strategy strategy)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(strategy, nameof(strategy));
        return shield.Append(strategy);
    }

    /// <summary>Wraps <paramref name="inner"/> inside <paramref name="outer"/>: the outer shield's strategies run first.</summary>
    public static Shield Wrap(this Shield outer, Shield inner)
    {
        Throw.IfNull(outer, nameof(outer));
        Throw.IfNull(inner, nameof(inner));
        return new Shield(Shield.Concat(outer.Strategies, inner.Strategies), outer.Ambient, outer.Name, outer.Time);
    }

    /// <summary>Wraps a result-aware <paramref name="inner"/> shield inside <paramref name="outer"/>, producing a result-aware shield.</summary>
    public static Shield<TResult> Wrap<TResult>(this Shield outer, Shield<TResult> inner)
    {
        Throw.IfNull(outer, nameof(outer));
        Throw.IfNull(inner, nameof(inner));
        return new Shield<TResult>(Shield.Concat(outer.Strategies, inner.Strategies), inner.Ambient, outer.Name ?? inner.Name, outer.Time ?? inner.Time);
    }

    /// <summary>
    /// Lifts this shield into a result-aware <see cref="Shield{TResult}"/> so result-handling
    /// clauses can be chained. An ambient exception-handling clause carries over and stays
    /// ambient for strategies chained on the result-aware shield.
    /// </summary>
    public static Shield<TResult> For<TResult>(this Shield shield)
    {
        Throw.IfNull(shield, nameof(shield));
        return new Shield<TResult>(shield.Strategies, shield.Ambient, shield.Name, shield.Time);
    }

    /// <summary>
    /// Runs <paramref name="fallback"/> in place of handled failures, receiving the handled
    /// exception. Applies to void executions only; result-returning executions fail with a
    /// descriptive <see cref="InvalidOperationException"/> — use <c>Shield.For&lt;T&gt;().Fallback(…)</c> for those.
    /// </summary>
    public static Shield Fallback(this Shield shield, Func<Exception, CancellationToken, ValueTask> fallback)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(fallback, nameof(fallback));
        return shield.Append(new VoidFallbackStrategy(fallback, shield.JudgeOrDefault));
    }

    /// <summary>
    /// Runs <paramref name="fallback"/> in place of handled failures. Applies to void executions
    /// only; result-returning executions fail with a descriptive
    /// <see cref="InvalidOperationException"/> — use <c>Shield.For&lt;T&gt;().Fallback(…)</c> for those.
    /// </summary>
    public static Shield Fallback(this Shield shield, Func<CancellationToken, ValueTask> fallback)
    {
        Throw.IfNull(fallback, nameof(fallback));
        return shield.Fallback((_, token) => fallback(token));
    }

    /// <summary>Returns a copy of this shield with a diagnostic name (surfaced as <see cref="KevlarContext.ShieldName"/>).</summary>
    public static Shield WithName(this Shield shield, string name)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(name, nameof(name));
        return new Shield(shield.Strategies, shield.Ambient, name, shield.Time);
    }

    /// <summary>Returns a copy of this shield using the given <see cref="TimeProvider"/> for delays, timeouts and time windows.</summary>
    public static Shield WithTimeProvider(this Shield shield, TimeProvider timeProvider)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(timeProvider, nameof(timeProvider));
        return new Shield(shield.Strategies, shield.Ambient, shield.Name, timeProvider);
    }
}
