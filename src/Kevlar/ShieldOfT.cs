using Kevlar.Internal;
using Kevlar.Strategies;

namespace Kevlar;

/// <summary>
/// An immutable, thread-safe, result-aware resilience pipeline for executions returning
/// <typeparamref name="TResult"/>. Unlike <see cref="Shield"/>, its handling clauses can react to
/// result values (<c>WhenResult</c>) as well as exceptions. The first strategy in a chain is the
/// outermost.
/// </summary>
public sealed class Shield<TResult>
{
    internal readonly Strategy[] Strategies;
    internal readonly StrategyNode? Head;
    internal readonly OutcomeJudge? Ambient;
    internal readonly TimeProvider? Time;

    internal Shield(Strategy[] strategies, OutcomeJudge? ambient, string? name, TimeProvider? timeProvider)
    {
        Shield.ValidateChain(strategies);
        Strategies = strategies;
        Head = Shield.BuildChain(strategies);
        Ambient = ambient;
        Name = name;
        Time = timeProvider;
    }

    /// <summary>The shield's diagnostic name, if assigned via <see cref="WithName"/>.</summary>
    public string? Name { get; }

    /// <summary>A shield with no strategies: executions pass straight through.</summary>
    public static Shield<TResult> Empty { get; } = new([], null, null, null);

    internal OutcomeJudge JudgeOrDefault => Ambient ?? OutcomeJudge.Default;

    internal TimeProvider TimeOrSystem => Time ?? TimeProvider.System;

    // ── Handling clauses ────────────────────────────────────────────────────────────────

    /// <summary>Starts a handling clause: subsequent reactive strategies act on exceptions of type <typeparamref name="TException"/>.</summary>
    public ShieldBuilder<TResult> When<TException>()
        where TException : Exception
        => new ShieldBuilder<TResult>(this).Or<TException>();

    /// <summary>Starts a handling clause for exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>.</summary>
    public ShieldBuilder<TResult> When<TException>(Func<TException, bool> predicate)
        where TException : Exception
        => new ShieldBuilder<TResult>(this).Or(predicate);

    /// <summary>Starts a handling clause for exceptions matching <paramref name="predicate"/>.</summary>
    public ShieldBuilder<TResult> When(Func<Exception, bool> predicate) => new ShieldBuilder<TResult>(this).OrWhen(predicate);

    /// <summary>Starts a handling clause for results matching <paramref name="predicate"/>.</summary>
    public ShieldBuilder<TResult> WhenResult(Func<TResult, bool> predicate) => new ShieldBuilder<TResult>(this).OrResult(predicate);

    /// <summary>Starts a handling clause for results equal to <paramref name="result"/>.</summary>
    public ShieldBuilder<TResult> WhenResult(TResult result) => new ShieldBuilder<TResult>(this).OrResult(result);

    /// <summary>Starts a handling clause for results equal to <c>default</c> — <see langword="null"/> for reference types.</summary>
    public ShieldBuilder<TResult> WhenDefault() => new ShieldBuilder<TResult>(this).OrDefault();

    // ── Strategy chaining ───────────────────────────────────────────────────────────────

    /// <summary>Retries handled outcomes up to <paramref name="maxRetries"/> times with the default exponential jittered backoff.</summary>
    public Shield<TResult> Retry(int maxRetries = 3) => Retry(options => options.MaxRetries = maxRetries);

    /// <summary>Retries handled outcomes up to <paramref name="maxRetries"/> times with the given backoff.</summary>
    public Shield<TResult> Retry(int maxRetries, Backoff backoff) => Retry(options =>
    {
        options.MaxRetries = maxRetries;
        options.Backoff = backoff;
    });

    /// <summary>
    /// Adds a retry strategy configured via <paramref name="configure"/>. The options expose
    /// result-typed events: <c>OnRetry</c>, <c>OnRetryAsync</c>, <c>DelayGenerator</c>, and
    /// <c>DelayGeneratorAsync</c> receive a <see cref="RetryEvent{TResult}"/> carrying the handled
    /// <see cref="Outcome{TResult}"/>.
    /// </summary>
    public Shield<TResult> Retry(Action<RetryOptions<TResult>> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        var options = new RetryOptions<TResult>();
        configure(options);
        return Append(RetryStrategy.Create(options, JudgeOrDefault));
    }

    /// <summary>Retries handled outcomes indefinitely.</summary>
    public Shield<TResult> RetryForever(Backoff? backoff = null) => Retry(options =>
    {
        options.MaxRetries = int.MaxValue;
        options.Backoff = backoff ?? Backoff.Default;
    });

    /// <summary>Cancels executions that exceed <paramref name="timeout"/>, surfacing <see cref="TimeoutExceededException"/>.</summary>
    public Shield<TResult> Timeout(TimeSpan timeout) => Timeout(options => options.Timeout = timeout);

    /// <summary>Adds a timeout strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> Timeout(Action<TimeoutOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        var options = new TimeoutOptions();
        configure(options);
        return Append(new TimeoutStrategy(options));
    }

    /// <summary>Breaks the circuit for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive handled outcomes.</summary>
    public Shield<TResult> CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration) => CircuitBreaker(options =>
    {
        options.ConsecutiveFailures = consecutiveFailures;
        options.BreakDuration = breakDuration;
    });

    /// <summary>Adds a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> CircuitBreaker(Action<CircuitBreakerOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        var options = new CircuitBreakerOptions();
        configure(options);
        return Append(new CircuitBreakerStrategy(options, JudgeOrDefault));
    }

    /// <summary>Limits throughput to <paramref name="permits"/> executions per <paramref name="perWindow"/> (token bucket).</summary>
    public Shield<TResult> RateLimit(int permits, TimeSpan perWindow) => RateLimit(options =>
    {
        options.Permits = permits;
        options.Window = perWindow;
    });

    /// <summary>Adds a rate limit strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> RateLimit(Action<RateLimitOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        var options = new RateLimitOptions();
        configure(options);
        return Append(new RateLimitStrategy(options));
    }

    /// <summary>Caps concurrent executions at <paramref name="maxConcurrency"/> with an optional wait queue.</summary>
    public Shield<TResult> ConcurrencyLimit(int maxConcurrency, int maxQueue = 0) => ConcurrencyLimit(options =>
    {
        options.MaxConcurrency = maxConcurrency;
        options.MaxQueue = maxQueue;
    });

    /// <summary>Adds a concurrency limit strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> ConcurrencyLimit(Action<ConcurrencyLimitOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        var options = new ConcurrencyLimitOptions();
        configure(options);
        return Append(new ConcurrencyLimitStrategy(options));
    }

    /// <summary>Races up to <paramref name="maxAttempts"/> concurrent attempts staggered by <paramref name="delay"/>; first acceptable outcome wins.</summary>
    public Shield<TResult> Hedge(int maxAttempts, TimeSpan delay) => Hedge(options =>
    {
        options.MaxAttempts = maxAttempts;
        options.Delay = delay;
    });

    /// <summary>Adds a hedging strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> Hedge(Action<HedgingOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        var options = new HedgingOptions();
        configure(options);
        return Append(new HedgingStrategy(options, JudgeOrDefault));
    }

    /// <summary>Replaces handled outcomes with <paramref name="fallbackValue"/>.</summary>
    public Shield<TResult> Fallback(TResult fallbackValue, Action<FallbackEvent<TResult>>? onFallback = null) =>
        Append(new FallbackStrategy<TResult>((_, _) => new ValueTask<TResult>(fallbackValue), JudgeOrDefault, onFallback, null));

    /// <summary>Replaces handled outcomes with <paramref name="fallbackValue"/> and uses the configured notifications.</summary>
    public Shield<TResult> FallbackWithNotifications(TResult fallbackValue, FallbackOptions<TResult> options)
    {
        Throw.IfNull(options, nameof(options));
        return Append(new FallbackStrategy<TResult>(
            (_, _) => new ValueTask<TResult>(fallbackValue),
            JudgeOrDefault,
            options.OnFallback,
            options.OnFallbackAsync));
    }

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/>.</summary>
    public Shield<TResult> Fallback(Func<CancellationToken, ValueTask<TResult>> fallback, Action<FallbackEvent<TResult>>? onFallback = null)
    {
        Throw.IfNull(fallback, nameof(fallback));
        return Append(new FallbackStrategy<TResult>((_, context) => fallback(context.CancellationToken), JudgeOrDefault, onFallback, null));
    }

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/> and uses the configured notifications.</summary>
    public Shield<TResult> FallbackWithNotifications(
        Func<CancellationToken, ValueTask<TResult>> fallback,
        FallbackOptions<TResult> options)
    {
        Throw.IfNull(fallback, nameof(fallback));
        Throw.IfNull(options, nameof(options));
        return Append(new FallbackStrategy<TResult>(
            (_, context) => fallback(context.CancellationToken),
            JudgeOrDefault,
            options.OnFallback,
            options.OnFallbackAsync));
    }

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/>, which receives the handled outcome.</summary>
    public Shield<TResult> Fallback(Func<Outcome<TResult>, CancellationToken, ValueTask<TResult>> fallback, Action<FallbackEvent<TResult>>? onFallback = null)
    {
        Throw.IfNull(fallback, nameof(fallback));
        return Append(new FallbackStrategy<TResult>((outcome, context) => fallback(outcome, context.CancellationToken), JudgeOrDefault, onFallback, null));
    }

    /// <summary>
    /// Replaces handled outcomes with the result of <paramref name="fallback"/>, which receives
    /// the handled outcome, and uses the configured notifications.
    /// </summary>
    public Shield<TResult> FallbackWithNotifications(
        Func<Outcome<TResult>, CancellationToken, ValueTask<TResult>> fallback,
        FallbackOptions<TResult> options)
    {
        Throw.IfNull(fallback, nameof(fallback));
        Throw.IfNull(options, nameof(options));
        return Append(new FallbackStrategy<TResult>(
            (outcome, context) => fallback(outcome, context.CancellationToken),
            JudgeOrDefault,
            options.OnFallback,
            options.OnFallbackAsync));
    }

    /// <summary>Appends a custom <see cref="Strategy"/> implementation to the pipeline.</summary>
    public Shield<TResult> Use(Strategy strategy)
    {
        Throw.IfNull(strategy, nameof(strategy));
        return Append(strategy);
    }

    /// <summary>
    /// Appends a custom strategy created from the active handling clause. The factory runs once;
    /// reactive custom strategies should retain and consult the supplied clause.
    /// </summary>
    public Shield<TResult> Use(Func<HandlingClause, Strategy> factory)
    {
        Throw.IfNull(factory, nameof(factory));
        var strategy = factory(new HandlingClause(JudgeOrDefault))
            ?? throw new InvalidOperationException("The strategy factory returned null.");
        return Append(strategy);
    }

    // ── Composition ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps <paramref name="inner"/> inside this shield: this shield's strategies run outermost.
    /// The first non-null name and time provider win; the last handling clause stays ambient.
    /// </summary>
    public Shield<TResult> Wrap(Shield inner)
    {
        Throw.IfNull(inner, nameof(inner));
        return new Shield<TResult>(
            Shield.Concat(Strategies, inner.Strategies),
            inner.Ambient ?? Ambient,
            Name ?? inner.Name,
            Time ?? inner.Time);
    }

    /// <summary>
    /// Wraps <paramref name="inner"/> inside this shield: this shield's strategies run outermost.
    /// The first non-null name and time provider win; the last handling clause stays ambient.
    /// </summary>
    public Shield<TResult> Wrap(Shield<TResult> inner)
    {
        Throw.IfNull(inner, nameof(inner));
        return new Shield<TResult>(
            Shield.Concat(Strategies, inner.Strategies),
            inner.Ambient ?? Ambient,
            Name ?? inner.Name,
            Time ?? inner.Time);
    }

    /// <summary>Returns a copy of this shield with a diagnostic name (surfaced as <see cref="KevlarContext.ShieldName"/>).</summary>
    public Shield<TResult> WithName(string name)
    {
        Throw.IfNull(name, nameof(name));
        return new Shield<TResult>(Strategies, Ambient, name, Time);
    }

    /// <summary>Returns a copy of this shield using the given <see cref="TimeProvider"/> for delays, timeouts and time windows.</summary>
    public Shield<TResult> WithTimeProvider(TimeProvider timeProvider)
    {
        Throw.IfNull(timeProvider, nameof(timeProvider));
        return new Shield<TResult>(Strategies, Ambient, Name, timeProvider);
    }

    // ── Execution ───────────────────────────────────────────────────────────────────────

    /// <summary>Executes the delegate through the pipeline. The delegate must use the cancellation token it is handed.</summary>
    public ValueTask<TResult> ExecuteAsync(Func<CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteAsync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>Executes the delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public ValueTask<TResult> ExecuteAsync<TState>(TState state, Func<TState, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteAsync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>
    /// Initializes execution properties, then executes a context-aware delegate through the pipeline.
    /// The context is pooled and is valid only for the duration of the delegate invocation; never retain it.
    /// </summary>
    public ValueTask<TResult> ExecuteWithContextAsync<TState>(
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, ValueTask<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteWithContextAsync(
            Head,
            TimeOrSystem,
            Name,
            state,
            initializeProperties,
            action,
            cancellationToken);
    }

    /// <summary>Executes the delegate through the pipeline and returns the outcome instead of throwing.</summary>
    public ValueTask<Outcome<TResult>> ExecuteOutcomeAsync(Func<CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteOutcomeAsync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>
    /// Executes the delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations,
    /// and returns the outcome instead of throwing.
    /// </summary>
    public ValueTask<Outcome<TResult>> ExecuteOutcomeAsync<TState>(TState state, Func<TState, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteOutcomeAsync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>
    /// Executes the delegate synchronously through the pipeline. Delays block the calling thread.
    /// Hedging is not supported synchronously.
    /// </summary>
    public TResult Execute(Func<CancellationToken, TResult> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteSync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>Executes the delegate synchronously, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public TResult Execute<TState>(TState state, Func<TState, CancellationToken, TResult> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteSync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>
    /// Initializes execution properties, then executes a context-aware delegate synchronously through the pipeline.
    /// The context is pooled and is valid only for the duration of the delegate invocation; never retain it.
    /// </summary>
    public TResult ExecuteWithContext<TState>(
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, TResult> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteWithContextSync(
            Head,
            TimeOrSystem,
            Name,
            state,
            initializeProperties,
            action,
            cancellationToken);
    }

    /// <summary>Describes the pipeline, outermost strategy first, like <see cref="Shield.ToString"/>.</summary>
    public override string ToString() => Shield.Describe(Name, Strategies);

    // ── Internals ───────────────────────────────────────────────────────────────────────

    internal Shield<TResult> Append(Strategy strategy, OutcomeJudge? ambient = null)
    {
        var strategies = new Strategy[Strategies.Length + 1];
        Array.Copy(Strategies, strategies, Strategies.Length);
        strategies[Strategies.Length] = strategy;
        return new Shield<TResult>(strategies, ambient ?? Ambient, Name, Time);
    }
}
