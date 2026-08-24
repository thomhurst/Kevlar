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
    /// <param name="shield">The shield to append the retry to.</param>
    /// <param name="maxRetries">
    /// The number of <em>retries</em>, not the number of attempts: <c>Retry(3)</c> makes up to 4
    /// total attempts — the initial call plus 3 retries.
    /// </param>
    public static Shield Retry(this Shield shield, int maxRetries = 3) => shield.Retry(options => options.MaxRetries = maxRetries);

    /// <summary>Appends a retry of handled exceptions, up to <paramref name="maxRetries"/> times, with the given backoff.</summary>
    /// <param name="shield">The shield to append the retry to.</param>
    /// <param name="maxRetries">
    /// The number of <em>retries</em>, not the number of attempts: <c>Retry(3)</c> makes up to 4
    /// total attempts — the initial call plus 3 retries.
    /// </param>
    /// <param name="backoff">The delay computation applied between attempts.</param>
    public static Shield Retry(this Shield shield, int maxRetries, Backoff backoff) => shield.Retry(options =>
    {
        options.MaxRetries = maxRetries;
        options.Backoff = backoff;
    });

    /// <summary>Appends a retry strategy configured via <paramref name="configure"/>.</summary>
    /// <remarks>
    /// <see cref="RetryOptions.MaxRetries"/> counts <em>retries</em>, not attempts:
    /// <c>MaxRetries = 3</c> makes up to 4 total attempts — the initial call plus 3 retries.
    /// </remarks>
    public static Shield Retry(this Shield shield, Action<RetryOptions> configure)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(configure, nameof(configure));
        var options = new RetryOptions();
        configure(options);
        var judge = HandlingOverride.Resolve(options.HandlesException, shield.JudgeOrDefault);
        return shield.Append(new RetryStrategy(options, judge));
    }

    /// <summary>Appends a retry that never gives up, with the default exponential jittered backoff.</summary>
    /// <param name="shield">The shield to append the retry to.</param>
    public static Shield RetryForever(this Shield shield) => shield.RetryForever(Backoff.Default);

    /// <summary>Appends a retry that never gives up, with the given backoff.</summary>
    /// <param name="shield">The shield to append the retry to.</param>
    /// <param name="backoff">The delay computation applied between attempts.</param>
    public static Shield RetryForever(this Shield shield, Backoff backoff)
    {
        Throw.IfNull(backoff, nameof(backoff));
        return shield.Retry(options =>
        {
            options.MaxRetries = int.MaxValue;
            options.Backoff = backoff;
        });
    }

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
        var judge = HandlingOverride.Resolve(options.HandlesException, shield.JudgeOrDefault);
        return shield.Append(new CircuitBreakerStrategy(options, judge));
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
    /// <remarks>
    /// Hedging on an untyped <see cref="Shield"/> runs the execution delegate more than once,
    /// concurrently, and only its exceptions can select a winner. The delegate must therefore be
    /// idempotent: duplicate writes, charges, or sends are otherwise observable side effects of a
    /// hedge that later loses. Prefer <c>Shield.For&lt;T&gt;()</c>, where result clauses decide which
    /// attempt is acceptable, or confirm the action is safe to repeat.
    /// </remarks>
    public static Shield Hedge(this Shield shield, int maxAttempts, TimeSpan delay) => shield.Hedge(options =>
    {
        options.MaxAttempts = maxAttempts;
        options.Delay = delay;
    });

    /// <summary>Appends a hedging strategy configured via <paramref name="configure"/>.</summary>
    /// <remarks>
    /// Hedging on an untyped <see cref="Shield"/> runs the execution delegate more than once,
    /// concurrently, so the delegate must be idempotent. Prefer <c>Shield.For&lt;T&gt;()</c>, or
    /// confirm the action is safe to repeat.
    /// </remarks>
    public static Shield Hedge(this Shield shield, Action<HedgeOptions> configure)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(configure, nameof(configure));
        var options = new HedgeOptions();
        configure(options);
        var judge = HandlingOverride.Resolve(options.HandlesException, shield.JudgeOrDefault);
        return shield.Append(new HedgingStrategy(options, judge));
    }

    /// <summary>Starts a handling clause: subsequent reactive strategies act on exceptions of type <typeparamref name="TException"/>. Use <see cref="WhenAnyError"/> to return to default handling.</summary>
    public static ShieldBuilder When<TException>(this Shield shield)
        where TException : Exception
    {
        Throw.IfNull(shield, nameof(shield));
        return new ShieldBuilder(shield).Or<TException>();
    }

    /// <summary>Starts a handling clause for exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>. Use <see cref="WhenAnyError"/> to return to default handling.</summary>
    public static ShieldBuilder When<TException>(this Shield shield, Func<TException, bool> predicate)
        where TException : Exception
    {
        Throw.IfNull(shield, nameof(shield));
        return new ShieldBuilder(shield).Or(predicate);
    }

    /// <summary>Starts a handling clause for exceptions matching <paramref name="predicate"/>. Use <see cref="WhenAnyError"/> to return to default handling.</summary>
    public static ShieldBuilder When(this Shield shield, Func<Exception, bool> predicate)
    {
        Throw.IfNull(shield, nameof(shield));
        return new ShieldBuilder(shield).Or(predicate);
    }

    /// <summary>
    /// Resets the ambient handling clause. Subsequent reactive strategies use the default
    /// handling defined by <see cref="HandlingClause.Default"/>.
    /// </summary>
    public static Shield WhenAnyError(this Shield shield)
    {
        Throw.IfNull(shield, nameof(shield));
        return new Shield(shield.Strategies, OutcomeJudge.Default, shield.Name, shield.Time);
    }

    /// <summary>Appends a custom <see cref="Strategy"/> implementation to the pipeline.</summary>
    public static Shield Use(this Shield shield, Strategy strategy)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(strategy, nameof(strategy));
        return shield.Append(strategy);
    }

    /// <summary>
    /// Appends a custom strategy created from the active handling clause. The factory runs once;
    /// reactive custom strategies should retain and consult the supplied clause.
    /// </summary>
    public static Shield Use(this Shield shield, Func<HandlingClause, Strategy> factory)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(factory, nameof(factory));
        var strategy = factory(new HandlingClause(shield.JudgeOrDefault))
            ?? throw new InvalidOperationException("The strategy factory returned null.");
        return shield.Append(strategy);
    }

    /// <summary>
    /// Wraps <paramref name="inner"/> inside <paramref name="outer"/>: the outer shield's strategies
    /// run first. The first non-null name and time provider win. Composition seals handling clauses,
    /// so reactive strategies appended afterwards use default handling unless a new clause is declared.
    /// </summary>
    public static Shield Wrap(this Shield outer, Shield inner)
    {
        Throw.IfNull(outer, nameof(outer));
        Throw.IfNull(inner, nameof(inner));
        return new Shield(
            Shield.Concat(outer.Strategies, inner.Strategies),
            null,
            outer.Name ?? inner.Name,
            outer.Time ?? inner.Time);
    }

    /// <summary>
    /// Wraps a void-only <paramref name="inner"/> shield inside <paramref name="outer"/>. Because
    /// the resulting pipeline contains a void fallback, it remains restricted to void executions.
    /// </summary>
    public static VoidShield Wrap(this Shield outer, VoidShield inner)
    {
        Throw.IfNull(outer, nameof(outer));
        Throw.IfNull(inner, nameof(inner));
        var pipeline = inner.Pipeline;
        return new VoidShield(new Shield(
            Shield.Concat(outer.Strategies, pipeline.Strategies),
            null,
            outer.Name ?? pipeline.Name,
            outer.Time ?? pipeline.Time));
    }

    /// <summary>
    /// Wraps a result-aware <paramref name="inner"/> shield inside <paramref name="outer"/>, producing
    /// a result-aware shield. The first non-null name and time provider win. Composition seals handling
    /// clauses, so reactive strategies appended afterwards use default handling unless a new clause is declared.
    /// </summary>
    public static Shield<TResult> Wrap<TResult>(this Shield outer, Shield<TResult> inner)
    {
        Throw.IfNull(outer, nameof(outer));
        Throw.IfNull(inner, nameof(inner));
        return new Shield<TResult>(
            Shield.Concat(outer.Strategies, inner.Strategies),
            null,
            outer.Name ?? inner.Name,
            outer.Time ?? inner.Time);
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
    /// exception. Returns a <see cref="VoidShield"/>, which exposes only void execution overloads.
    /// Use <c>Shield.For&lt;T&gt;().FallbackTo(…)</c> for constant values or its typed <c>Fallback(…)</c> overloads for factories.
    /// </summary>
    public static VoidShield Fallback(this Shield shield, Func<Exception, CancellationToken, ValueTask> fallback)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(fallback, nameof(fallback));
        return new VoidShield(shield.Append(new VoidFallbackStrategy(fallback, shield.JudgeOrDefault, null, null)));
    }

    /// <summary>
    /// Runs <paramref name="fallback"/> in place of handled failures and configures notifications.
    /// Applies to void executions only.
    /// </summary>
    /// <remarks>Runs <see cref="FallbackOptions.OnFallback"/>, then <see cref="FallbackOptions.OnFallbackAsync"/>, before recovery. A notification failure skips recovery.</remarks>
    public static VoidShield Fallback(
        this Shield shield,
        Func<Exception, CancellationToken, ValueTask> fallback,
        Action<FallbackOptions> configure)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(fallback, nameof(fallback));
        Throw.IfNull(configure, nameof(configure));
        var options = new FallbackOptions();
        configure(options);
        var judge = HandlingOverride.Resolve(options.HandlesException, shield.JudgeOrDefault);
        return new VoidShield(shield.Append(new VoidFallbackStrategy(
            fallback,
            judge,
            options.OnFallback,
            options.OnFallbackAsync,
            options.HandlesException is not null)));
    }

    /// <summary>
    /// Runs <paramref name="fallback"/> in place of handled failures. Returns a
    /// <see cref="VoidShield"/>, which exposes only void execution overloads. Use
    /// <c>Shield.For&lt;T&gt;().FallbackTo(…)</c> for constant values or its typed <c>Fallback(…)</c> overloads for factories.
    /// </summary>
    public static VoidShield Fallback(this Shield shield, Func<CancellationToken, ValueTask> fallback)
    {
        Throw.IfNull(fallback, nameof(fallback));
        return shield.Fallback((_, token) => fallback(token));
    }

    /// <summary>
    /// Runs <paramref name="fallback"/> in place of handled failures and configures notifications.
    /// Applies to void executions only.
    /// </summary>
    /// <remarks>Runs <see cref="FallbackOptions.OnFallback"/>, then <see cref="FallbackOptions.OnFallbackAsync"/>, before recovery. A notification failure skips recovery.</remarks>
    public static VoidShield Fallback(
        this Shield shield,
        Func<CancellationToken, ValueTask> fallback,
        Action<FallbackOptions> configure)
    {
        Throw.IfNull(fallback, nameof(fallback));
        Throw.IfNull(configure, nameof(configure));
        return shield.Fallback((_, token) => fallback(token), configure);
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
