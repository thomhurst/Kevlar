using Kevlar.Internal;
using Kevlar.Strategies;

#pragma warning disable RS0026, RS0027 // Execution overload parity intentionally keeps CancellationToken optional.

namespace Kevlar;

/// <summary>
/// An immutable, thread-safe, result-aware resilience pipeline for executions returning
/// <typeparamref name="TResult"/>. Unlike <see cref="Shield"/>, its handling clauses can react to
/// result values (<c>WhenResult</c>) as well as exceptions. The first strategy in a chain is the
/// outermost.
/// </summary>
public sealed class Shield<TResult> : IShieldLifecycle
{
    internal readonly Strategy[] Strategies;
    internal readonly StrategyNode? Head;
    internal readonly OutcomeJudge? Ambient;
    internal readonly TimeProvider? Time;
    internal readonly IShieldDecorator[] AppliedDecorators;
    private readonly StrategyOwnerSet _strategyOwners;
    private readonly Func<Shield<TResult>>? _currentSnapshot;
    private readonly string? _name;

    Strategy[] IShieldLifecycle.Strategies => CurrentSnapshot.Strategies;

    internal Shield(
        Strategy[] strategies,
        OutcomeJudge? ambient,
        string? name,
        TimeProvider? timeProvider,
        IShieldDecorator[]? appliedDecorators = null)
    {
        Shield.ValidateChain(strategies);
        foreach (var strategy in strategies)
        {
            if (strategy is HedgingStrategy hedging)
            {
                hedging.ValidateResultType(typeof(TResult));
            }

            if (strategy is VoidFallbackStrategy)
            {
                throw Shield.CreateVoidFallbackResultException();
            }
        }

        Strategies = strategies;
        _strategyOwners = Shield.GetStrategyOwners(strategies);
        Head = Shield.BuildChain(strategies, _strategyOwners);
        Ambient = ambient;
        _name = name;
        Time = timeProvider;
        AppliedDecorators = appliedDecorators ?? [];
    }

    internal Shield(Func<Shield<TResult>> currentSnapshot)
        : this([], null, null, null)
    {
        _currentSnapshot = currentSnapshot;
    }

    /// <summary>The shield's diagnostic name, if assigned via <see cref="WithName"/>.</summary>
    public string? Name => _currentSnapshot is { } currentSnapshot
        ? currentSnapshot().Name
        : _name;

    internal Shield<TResult> CurrentSnapshot => _currentSnapshot?.Invoke() ?? this;

    /// <summary>A shield with no strategies: executions pass straight through.</summary>
    public static Shield<TResult> Empty { get; } = new([], null, null, null);

    /// <summary>
    /// Gets whether every strategy guarantees invoking the execution continuation at most once.
    /// Custom strategies may opt in through <see cref="Strategy.InvokesContinuationAtMostOnce"/>.
    /// </summary>
    /// <remarks>
    /// A live-forwarding shield reports <see langword="false"/> because a later publication may
    /// introduce retry, hedging, or another multi-attempt strategy.
    /// </remarks>
    public bool InvokesContinuationAtMostOnce =>
        _currentSnapshot is null
        && Strategies.All(static strategy => strategy.InvokesContinuationAtMostOnce);

    internal OutcomeJudge JudgeOrDefault => CurrentSnapshot.Ambient ?? OutcomeJudge.Default;

    internal OutcomeJudge FallbackJudgeOrDefault =>
        CurrentSnapshot.Ambient is not { } ambient || ReferenceEquals(ambient, OutcomeJudge.Default)
            ? OutcomeJudge.FallbackDefault
            : ambient;

    internal TimeProvider TimeOrSystem => CurrentSnapshot.Time ?? TimeProvider.System;

    // ── Handling clauses ────────────────────────────────────────────────────────────────

    /// <summary>Starts a handling clause: subsequent reactive strategies act on exceptions of type <typeparamref name="TException"/>. Use <see cref="WithDefaultHandling"/> to return to default handling.</summary>
    public ShieldBuilder<TResult> When<TException>()
        where TException : Exception
        => new ShieldBuilder<TResult>(this).Or<TException>();

    /// <summary>Starts a handling clause for exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>. Use <see cref="WithDefaultHandling"/> to return to default handling.</summary>
    public ShieldBuilder<TResult> When<TException>(Func<TException, bool> predicate)
        where TException : Exception
        => new ShieldBuilder<TResult>(this).Or(predicate);

    /// <summary>Starts a handling clause for exceptions matching <paramref name="predicate"/>. Use <see cref="WithDefaultHandling"/> to return to default handling.</summary>
    public ShieldBuilder<TResult> When(Func<Exception, bool> predicate) => new ShieldBuilder<TResult>(this).Or(predicate);

    /// <summary>Starts a handling clause using the typed outcome and active execution context.</summary>
    public ShieldBuilder<TResult> WhenContext(Func<HandlingEvent<TResult>, bool> predicate) =>
        new ShieldBuilder<TResult>(this).OrContext(predicate);

    /// <summary>Starts a handling clause for results matching <paramref name="predicate"/>. Use <see cref="WithDefaultHandling"/> to return to default handling.</summary>
    public ShieldBuilder<TResult> WhenResult(Func<TResult, bool> predicate) => new ShieldBuilder<TResult>(this).OrResult(predicate);

    /// <summary>Starts a result handling clause using the typed outcome and active execution context.</summary>
    public ShieldBuilder<TResult> WhenResultContext(Func<HandlingEvent<TResult>, bool> predicate) =>
        new ShieldBuilder<TResult>(this).OrResultContext(predicate);

    /// <summary>Starts a handling clause for results equal to <paramref name="result"/>. Use <see cref="WithDefaultHandling"/> to return to default handling.</summary>
    public ShieldBuilder<TResult> WhenResultEquals(TResult result) => new ShieldBuilder<TResult>(this).OrResultEquals(result);

    /// <summary>
    /// Starts a handling clause for results equal to <c>default(TResult)</c> — <see langword="null"/>
    /// for reference types. Use <see cref="WithDefaultHandling"/> to return to default handling.
    /// </summary>
    /// <remarks>
    /// For a reference type prefer <see cref="ShieldResultExtensions.WhenResultIsNull{TResult}(Shield{TResult})"/>,
    /// which says what it matches. This method stays for value types and generic code, where
    /// <c>default(TResult)</c> — <c>0</c>, <see langword="false"/> — may or may not be a failure.
    /// </remarks>
    public ShieldBuilder<TResult> WhenResultIsDefault() => new ShieldBuilder<TResult>(this).OrResultIsDefault();

    /// <summary>
    /// Resets the ambient handling clause. Subsequent reactive strategies use the default
    /// handling defined by <see cref="HandlingClause.Default"/>.
    /// </summary>
    public Shield<TResult> WithDefaultHandling()
    {
        var snapshot = CurrentSnapshot;
        return new(
            snapshot.Strategies,
            OutcomeJudge.Default,
            snapshot.Name,
            snapshot.Time,
            snapshot.AppliedDecorators);
    }

    // ── Strategy chaining ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Retries handled outcomes up to <paramref name="maxRetries"/> times with
    /// <see cref="Backoff.Default"/>: exponential from 250 milliseconds with factor 2, equal
    /// jitter, and a 30-second cap.
    /// </summary>
    /// <param name="maxRetries">
    /// The number of <em>retries</em>, not the number of attempts: <c>Retry(3)</c> makes up to 4
    /// total attempts — the initial call plus 3 retries.
    /// </param>
    public Shield<TResult> Retry(int maxRetries = 3)
    {
        Throw.IfOutOfRange(maxRetries < 0, nameof(maxRetries), "Max retries must not be negative.");
        return Retry(options => options.MaxRetries = maxRetries);
    }

    /// <summary>Retries handled outcomes up to <paramref name="maxRetries"/> times with the given backoff.</summary>
    /// <param name="maxRetries">
    /// The number of <em>retries</em>, not the number of attempts: <c>Retry(3)</c> makes up to 4
    /// total attempts — the initial call plus 3 retries.
    /// </param>
    /// <param name="backoff">The delay computation applied between attempts.</param>
    public Shield<TResult> Retry(int maxRetries, Backoff backoff)
    {
        Throw.IfOutOfRange(maxRetries < 0, nameof(maxRetries), "Max retries must not be negative.");
        Throw.IfNull(backoff, nameof(backoff));
        return Retry(options =>
        {
            options.MaxRetries = maxRetries;
            options.Backoff = backoff;
        });
    }

    /// <summary>
    /// Adds a retry strategy configured via <paramref name="configure"/>. The options expose
    /// result-typed events: <c>OnRetry</c> and <c>DelayGenerator</c> receive a
    /// <see cref="RetryEvent{TResult}"/> carrying the handled <see cref="Outcome{TResult}"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="RetryOptions{TResult}.MaxRetries"/> counts <em>retries</em>, not attempts:
    /// <c>MaxRetries = 3</c> makes up to 4 total attempts — the initial call plus 3 retries.
    /// </remarks>
    public Shield<TResult> Retry(Action<RetryOptions<TResult>> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        if (CurrentSnapshot is { } snapshot && !ReferenceEquals(snapshot, this))
        {
            return snapshot.Retry(configure);
        }

        var options = new RetryOptions<TResult>();
        configure(options);
        var judge = HandlingOverride.Resolve(
            options.HandlesException,
            options.HandlesResult,
            options.HandlesExceptionContext,
            options.HandlesResultContext,
            JudgeOrDefault);
        return Append(RetryStrategy.Create(options, judge));
    }

    /// <summary>
    /// Retries handled outcomes indefinitely with <see cref="Backoff.Default"/>: exponential
    /// from 250 milliseconds with factor 2, equal jitter, and a 30-second cap.
    /// </summary>
    public Shield<TResult> RetryForever() => RetryForever(Backoff.Default);

    /// <summary>Retries handled outcomes indefinitely with the given backoff.</summary>
    /// <param name="backoff">The delay computation applied between attempts.</param>
    public Shield<TResult> RetryForever(Backoff backoff)
    {
        Throw.IfNull(backoff, nameof(backoff));
        return Retry(options =>
        {
            options.MaxRetries = int.MaxValue;
            options.Backoff = backoff;
        });
    }

    /// <summary>Cancels executions that exceed <paramref name="timeout"/>, surfacing <see cref="TimeoutExceededException"/>.</summary>
    public Shield<TResult> Timeout(TimeSpan timeout)
    {
        Throw.IfOutOfRange(timeout <= TimeSpan.Zero, nameof(timeout), "Timeout must be positive.");
        Throw.IfOutOfRange(timeout > DelayHelper.MaximumDelay, nameof(timeout), "Timeout exceeds the runtime timer limit.");
        return Timeout(options => options.Timeout = timeout);
    }

    /// <summary>Adds a timeout strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> Timeout(Action<TimeoutOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        if (CurrentSnapshot is { } snapshot && !ReferenceEquals(snapshot, this))
        {
            return snapshot.Timeout(configure);
        }

        var options = new TimeoutOptions();
        configure(options);
        return Append(new TimeoutStrategy(options));
    }

    /// <summary>Breaks the circuit for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive handled outcomes.</summary>
    public Shield<TResult> CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration)
    {
        Throw.IfOutOfRange(consecutiveFailures <= 0, nameof(consecutiveFailures), "Consecutive failures must be positive.");
        Throw.IfOutOfRange(breakDuration <= TimeSpan.Zero, nameof(breakDuration), "Break duration must be positive.");
        return CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = consecutiveFailures;
            options.BreakDuration = breakDuration;
        });
    }

    /// <summary>Adds a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> CircuitBreaker(Action<CircuitBreakerOptions<TResult>> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        if (CurrentSnapshot is { } snapshot && !ReferenceEquals(snapshot, this))
        {
            return snapshot.CircuitBreaker(configure);
        }

        var options = new CircuitBreakerOptions<TResult>();
        configure(options);
        var judge = HandlingOverride.Resolve(
            options.HandlesException,
            options.HandlesResult,
            options.HandlesExceptionContext,
            options.HandlesResultContext,
            JudgeOrDefault);
        return Append(CircuitBreakerStrategy.Create(options, judge));
    }

    /// <summary>Limits throughput to <paramref name="permits"/> executions per <paramref name="perWindow"/> (token bucket).</summary>
    public Shield<TResult> RateLimit(int permits, TimeSpan perWindow)
    {
        Throw.IfOutOfRange(permits <= 0, nameof(permits), "Permits must be positive.");
        Throw.IfOutOfRange(perWindow <= TimeSpan.Zero, nameof(perWindow), "Window must be positive.");
        return RateLimit(options =>
        {
            options.Permits = permits;
            options.Window = perWindow;
        });
    }

    /// <summary>Adds a rate limit strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> RateLimit(Action<RateLimitOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        if (CurrentSnapshot is { } snapshot && !ReferenceEquals(snapshot, this))
        {
            return snapshot.RateLimit(configure);
        }

        var options = new RateLimitOptions();
        configure(options);
        return Append(new RateLimitStrategy(options));
    }

    /// <summary>Caps concurrent executions at <paramref name="maxConcurrency"/> with an optional wait queue.</summary>
    public Shield<TResult> ConcurrencyLimit(int maxConcurrency, int queueLimit = 0)
    {
        Throw.IfOutOfRange(maxConcurrency <= 0, nameof(maxConcurrency), "MaxConcurrency must be positive.");
        Throw.IfOutOfRange(queueLimit < 0, nameof(queueLimit), "QueueLimit must not be negative.");
        return ConcurrencyLimit(options =>
        {
            options.MaxConcurrency = maxConcurrency;
            options.QueueLimit = queueLimit;
        });
    }

    /// <summary>Adds a concurrency limit strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> ConcurrencyLimit(Action<ConcurrencyLimitOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        if (CurrentSnapshot is { } snapshot && !ReferenceEquals(snapshot, this))
        {
            return snapshot.ConcurrencyLimit(configure);
        }

        var options = new ConcurrencyLimitOptions();
        configure(options);
        return Append(new ConcurrencyLimitStrategy(options));
    }

    /// <summary>Races the primary with up to <paramref name="maxHedgedAttempts"/> additional attempts staggered by <paramref name="delay"/>; first acceptable outcome wins.</summary>
    /// <param name="maxHedgedAttempts">Maximum additional attempts after the primary attempt.</param>
    /// <param name="delay">
    /// The delay between attempts. Zero launches attempts in parallel; any negative value launches
    /// another attempt only after a handled failure.
    /// </param>
    public Shield<TResult> Hedge(int maxHedgedAttempts, TimeSpan delay)
    {
        Throw.IfOutOfRange(
            maxHedgedAttempts < 0,
            nameof(maxHedgedAttempts),
            "Maximum hedged attempts must be non-negative.");
        Throw.IfOutOfRange(delay > DelayHelper.MaximumDelay, nameof(delay), "Delay exceeds the runtime timer limit.");
        return Hedge(options =>
        {
            options.MaxHedgedAttempts = maxHedgedAttempts;
            options.Delay = delay;
        });
    }

    /// <summary>Adds a hedging strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> Hedge(Action<HedgeOptions<TResult>> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        if (CurrentSnapshot is { } snapshot && !ReferenceEquals(snapshot, this))
        {
            return snapshot.Hedge(configure);
        }

        var options = new HedgeOptions<TResult>();
        configure(options);
        var judge = HandlingOverride.Resolve(
            options.HandlesException,
            options.HandlesResult,
            options.HandlesExceptionContext,
            options.HandlesResultContext,
            JudgeOrDefault);
        return Append(HedgingStrategy.Create(options, judge));
    }

    /// <summary>Replaces handled outcomes with <paramref name="fallbackValue"/>.</summary>
    public Shield<TResult> FallbackTo(TResult fallbackValue)
    {
        var snapshot = CurrentSnapshot;
        if (!ReferenceEquals(snapshot, this))
        {
            return snapshot.FallbackTo(fallbackValue);
        }

        return Append(new FallbackStrategy<TResult>(
            (_, _) => new ValueTask<TResult>(fallbackValue),
            FallbackJudgeOrDefault,
            null));
    }

    /// <summary>Replaces handled outcomes with <paramref name="fallbackValue"/> and configures notifications.</summary>
    /// <remarks>Runs and awaits <see cref="FallbackOptions{TResult}.OnFallback"/> before recovery. Notification failures are reported and recovery continues.</remarks>
    public Shield<TResult> FallbackTo(TResult fallbackValue, Action<FallbackOptions<TResult>> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        if (CurrentSnapshot is { } snapshot && !ReferenceEquals(snapshot, this))
        {
            return snapshot.FallbackTo(fallbackValue, configure);
        }

        var options = new FallbackOptions<TResult>();
        configure(options);
        var judge = HandlingOverride.Resolve(
            options.HandlesException,
            options.HandlesResult,
            options.HandlesExceptionContext,
            options.HandlesResultContext,
            FallbackJudgeOrDefault);
        return Append(new FallbackStrategy<TResult>(
            (_, _) => new ValueTask<TResult>(fallbackValue),
            judge,
            options.OnFallback,
            hasHandlingOverride: options.HasHandlingOverride,
            telemetryName: options.Name));
    }

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/>.</summary>
    public Shield<TResult> Fallback(Func<CancellationToken, ValueTask<TResult>> fallback)
    {
        Throw.IfNull(fallback, nameof(fallback));
        if (CurrentSnapshot is { } snapshot && !ReferenceEquals(snapshot, this))
        {
            return snapshot.Fallback(fallback);
        }

        return Append(new FallbackStrategy<TResult>(
            (_, context) => fallback(context.CancellationToken),
            FallbackJudgeOrDefault,
            null));
    }

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/> and configures notifications.</summary>
    /// <remarks>Runs and awaits <see cref="FallbackOptions{TResult}.OnFallback"/> before recovery. Notification failures are reported and recovery continues.</remarks>
    public Shield<TResult> Fallback(
        Func<CancellationToken, ValueTask<TResult>> fallback,
        Action<FallbackOptions<TResult>> configure)
    {
        Throw.IfNull(fallback, nameof(fallback));
        Throw.IfNull(configure, nameof(configure));
        if (CurrentSnapshot is { } snapshot && !ReferenceEquals(snapshot, this))
        {
            return snapshot.Fallback(fallback, configure);
        }

        var options = new FallbackOptions<TResult>();
        configure(options);
        var judge = HandlingOverride.Resolve(
            options.HandlesException,
            options.HandlesResult,
            options.HandlesExceptionContext,
            options.HandlesResultContext,
            FallbackJudgeOrDefault);
        return Append(new FallbackStrategy<TResult>(
            (_, context) => fallback(context.CancellationToken),
            judge,
            options.OnFallback,
            hasHandlingOverride: options.HasHandlingOverride,
            telemetryName: options.Name));
    }

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/>, which receives the handled outcome.</summary>
    public Shield<TResult> Fallback(Func<Outcome<TResult>, CancellationToken, ValueTask<TResult>> fallback)
    {
        Throw.IfNull(fallback, nameof(fallback));
        if (CurrentSnapshot is { } snapshot && !ReferenceEquals(snapshot, this))
        {
            return snapshot.Fallback(fallback);
        }

        return Append(new FallbackStrategy<TResult>(
            (outcome, context) => fallback(outcome, context.CancellationToken),
            FallbackJudgeOrDefault,
            null));
    }

    /// <summary>
    /// Replaces handled outcomes with the result of <paramref name="fallback"/>, which receives
    /// the handled outcome, and configures notifications.
    /// </summary>
    /// <remarks>Runs and awaits <see cref="FallbackOptions{TResult}.OnFallback"/> before recovery. Notification failures are reported and recovery continues.</remarks>
    public Shield<TResult> Fallback(
        Func<Outcome<TResult>, CancellationToken, ValueTask<TResult>> fallback,
        Action<FallbackOptions<TResult>> configure)
    {
        Throw.IfNull(fallback, nameof(fallback));
        Throw.IfNull(configure, nameof(configure));
        if (CurrentSnapshot is { } snapshot && !ReferenceEquals(snapshot, this))
        {
            return snapshot.Fallback(fallback, configure);
        }

        var options = new FallbackOptions<TResult>();
        configure(options);
        var judge = HandlingOverride.Resolve(
            options.HandlesException,
            options.HandlesResult,
            options.HandlesExceptionContext,
            options.HandlesResultContext,
            FallbackJudgeOrDefault);
        return Append(new FallbackStrategy<TResult>(
            (outcome, context) => fallback(outcome, context.CancellationToken),
            judge,
            options.OnFallback,
            hasHandlingOverride: options.HasHandlingOverride,
            telemetryName: options.Name));
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
        if (CurrentSnapshot is { } snapshot && !ReferenceEquals(snapshot, this))
        {
            return snapshot.Use(factory);
        }

        var strategy = factory(new HandlingClause(JudgeOrDefault))
            ?? throw new InvalidOperationException("The strategy factory returned null.");
        return Append(strategy);
    }

    // ── Composition ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps <paramref name="inner"/> inside this shield: this shield's strategies run outermost.
    /// The first non-null name and time provider win. Composition seals handling clauses, so reactive
    /// strategies appended afterwards use default handling unless a new clause is declared.
    /// </summary>
    public Shield<TResult> Wrap(Shield inner)
    {
        Throw.IfNull(inner, nameof(inner));
        var outer = CurrentSnapshot;
        inner = inner.CurrentSnapshot;
        var strategies = Shield.Concat(outer.Strategies, inner.Strategies);
        var wrapped = new Shield<TResult>(
            strategies,
            null,
            outer.Name ?? inner.Name,
            outer.Time ?? inner.Time,
            ShieldDecoration.MergeForComposition(
                outer.AppliedDecorators,
                ShieldDecoration.HasResilienceStrategies(outer.Strategies),
                inner.AppliedDecorators));
        StrategyAppendObserver.NotifyComposition(strategies, outer.Name ?? inner.Name, wrapped);
        return wrapped;
    }

    /// <summary>
    /// Wraps <paramref name="inner"/> inside this shield: this shield's strategies run outermost.
    /// The first non-null name and time provider win. Composition seals handling clauses, so reactive
    /// strategies appended afterwards use default handling unless a new clause is declared.
    /// </summary>
    public Shield<TResult> Wrap(Shield<TResult> inner)
    {
        Throw.IfNull(inner, nameof(inner));
        var outer = CurrentSnapshot;
        inner = inner.CurrentSnapshot;
        var strategies = Shield.Concat(outer.Strategies, inner.Strategies);
        var wrapped = new Shield<TResult>(
            strategies,
            null,
            outer.Name ?? inner.Name,
            outer.Time ?? inner.Time,
            ShieldDecoration.MergeForComposition(
                outer.AppliedDecorators,
                ShieldDecoration.HasResilienceStrategies(outer.Strategies),
                inner.AppliedDecorators));
        StrategyAppendObserver.NotifyComposition(strategies, outer.Name ?? inner.Name, wrapped);
        return wrapped;
    }

    /// <summary>
    /// Merges result-aware shields into one pipeline. The first shield is the outermost. Stateful
    /// strategies keep their identity, so a shared circuit breaker shield shares its circuit here.
    /// The result keeps the first non-null <see cref="Name"/> and <see cref="TimeProvider"/>
    /// among the inputs. Composition seals handling clauses, so reactive strategies appended
    /// afterwards use default handling unless a new clause is declared.
    /// </summary>
    public static Shield<TResult> Compose(params Shield<TResult>[] shields)
    {
        Throw.IfNull(shields, nameof(shields));

        var parts = new Strategy[shields.Length][];
        string? name = null;
        TimeProvider? time = null;
        IShieldDecorator[] appliedDecorators = [];
        var hasStrategies = false;

        for (var i = 0; i < shields.Length; i++)
        {
            var shield = shields[i];
            Throw.IfNull(shield, nameof(shields));
            shield = shield.CurrentSnapshot;
            parts[i] = shield.Strategies;
            name ??= shield.Name;
            time ??= shield.Time;
            var shieldHasStrategies = ShieldDecoration.HasResilienceStrategies(shield.Strategies);
            appliedDecorators = ShieldDecoration.MergeForComposition(
                appliedDecorators,
                hasStrategies,
                shield.AppliedDecorators);
            hasStrategies |= shieldHasStrategies;
        }

        var strategies = Shield.Concat(parts);
        var composed = new Shield<TResult>(strategies, null, name, time, appliedDecorators);
        StrategyAppendObserver.NotifyComposition(strategies, name, composed);
        return composed;
    }

    /// <summary>Returns a copy of this shield with a diagnostic name (surfaced as <see cref="KevlarContext.ShieldName"/>).</summary>
    public Shield<TResult> WithName(string name)
    {
        Throw.IfNull(name, nameof(name));
        var snapshot = CurrentSnapshot;
        var named = new Shield<TResult>(
            snapshot.Strategies,
            snapshot.Ambient,
            name,
            snapshot.Time,
            snapshot.AppliedDecorators);
        ShieldNameObserver.Notify(snapshot.Strategies, name, named);
        return named;
    }

    /// <summary>Returns a copy of this shield using the given <see cref="TimeProvider"/> for delays, timeouts and time windows.</summary>
    public Shield<TResult> WithTimeProvider(TimeProvider timeProvider)
    {
        Throw.IfNull(timeProvider, nameof(timeProvider));
        var snapshot = CurrentSnapshot;
        var timed = new Shield<TResult>(
            snapshot.Strategies,
            snapshot.Ambient,
            snapshot.Name,
            timeProvider,
            snapshot.AppliedDecorators);
        StrategyAppendObserver.NotifyComposition(snapshot.Strategies, snapshot.Name, timed);
        return timed;
    }

    // ── Execution ───────────────────────────────────────────────────────────────────────

    /// <summary>Executes the delegate through the pipeline. The delegate must use the cancellation token it is handed.</summary>
    public ValueTask<TResult> ExecuteAsync(Func<CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteAsync(action, cancellationToken);
        }

        return ShieldEngine.ExecuteAsync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>Executes the delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public ValueTask<TResult> ExecuteAsync<TState>(TState state, Func<TState, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteAsync(state, action, cancellationToken);
        }

        return ShieldEngine.ExecuteAsync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>
    /// Executes a context-aware delegate through the pipeline using the properties, cancellation
    /// token, and time provider from <paramref name="parentContext"/>.
    /// </summary>
    public ValueTask<TResult> ExecuteWithContextAsync(
        KevlarContext parentContext,
        Func<KevlarContext, ValueTask<TResult>> action)
    {
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(parentContext, nameof(parentContext));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteWithContextAsync(parentContext, action);
        }

        return ShieldEngine.ExecuteWithParentContextAsync(Head, Name, action, static (a, context) => a(context), parentContext);
    }

    /// <summary>
    /// Executes a context-aware delegate through the pipeline using the properties, cancellation
    /// token, and time provider from <paramref name="parentContext"/>, threading
    /// <paramref name="state"/> to avoid closure allocations.
    /// </summary>
    public ValueTask<TResult> ExecuteWithContextAsync<TState>(
        KevlarContext parentContext,
        TState state,
        Func<TState, KevlarContext, ValueTask<TResult>> action)
    {
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(parentContext, nameof(parentContext));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteWithContextAsync(parentContext, state, action);
        }

        return ShieldEngine.ExecuteWithParentContextAsync(Head, Name, state, action, parentContext);
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
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteWithContextAsync(
                state,
                initializeProperties,
                action,
                cancellationToken);
        }

        return ShieldEngine.ExecuteWithContextAsync(
            Head,
            TimeOrSystem,
            Name,
            state,
            initializeProperties,
            action,
            cancellationToken);
    }

    /// <summary>
    /// Initializes execution properties, executes a context-aware delegate, then exposes the final
    /// properties to <paramref name="onCompleted"/> before the pooled context is returned.
    /// </summary>
    public ValueTask<TResult> ExecuteWithContextAsync<TState>(
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, ValueTask<TResult>> action,
        Action<TState, KevlarProperties> onCompleted,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(onCompleted, nameof(onCompleted));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteWithContextAsync(
                state,
                initializeProperties,
                action,
                onCompleted,
                cancellationToken);
        }

        return ShieldEngine.ExecuteWithContextAsync(
            Head,
            TimeOrSystem,
            Name,
            state,
            initializeProperties,
            action,
            onCompleted,
            cancellationToken);
    }

    /// <summary>
    /// Executes a context-aware delegate through the pipeline without seeding execution properties.
    /// The context is pooled and is valid only for the duration of the delegate invocation; never retain it.
    /// </summary>
    public ValueTask<TResult> ExecuteWithContextAsync(
        Func<KevlarContext, ValueTask<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ExecuteWithContextAsync(
            action,
            static (_, _) => { },
            static (a, context) => a(context),
            cancellationToken);
    }

    /// <summary>Executes the delegate through the pipeline and returns the outcome instead of throwing.</summary>
    public ValueTask<Outcome<TResult>> ExecuteOutcomeAsync(Func<CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteOutcomeAsync(action, cancellationToken);
        }

        return ShieldEngine.ExecuteOutcomeAsync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>
    /// Executes the delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations,
    /// and returns the outcome instead of throwing.
    /// </summary>
    public ValueTask<Outcome<TResult>> ExecuteOutcomeAsync<TState>(TState state, Func<TState, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteOutcomeAsync(state, action, cancellationToken);
        }

        return ShieldEngine.ExecuteOutcomeAsync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>Executes the delegate synchronously and returns its outcome instead of throwing.</summary>
    public Outcome<TResult> ExecuteOutcome(
        Func<CancellationToken, TResult> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteOutcome(action, cancellationToken);
        }

        return ShieldEngine.ExecuteOutcomeSync(
            Head,
            TimeOrSystem,
            Name,
            action,
            static (a, token) => a(token),
            cancellationToken);
    }

    /// <summary>
    /// Executes the delegate synchronously, threading <paramref name="state"/> to avoid closure
    /// allocations, and returns its outcome instead of throwing.
    /// </summary>
    public Outcome<TResult> ExecuteOutcome<TState>(
        TState state,
        Func<TState, CancellationToken, TResult> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteOutcome(state, action, cancellationToken);
        }

        return ShieldEngine.ExecuteOutcomeSync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>
    /// Executes the delegate synchronously through the pipeline. Delays block the calling thread.
    /// Hedging is not supported synchronously.
    /// </summary>
    public TResult Execute(Func<CancellationToken, TResult> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().Execute(action, cancellationToken);
        }

        return ShieldEngine.ExecuteSync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>Executes the delegate synchronously, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public TResult Execute<TState>(TState state, Func<TState, CancellationToken, TResult> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().Execute(state, action, cancellationToken);
        }

        return ShieldEngine.ExecuteSync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>
    /// Executes a context-aware delegate synchronously through the pipeline using the properties,
    /// cancellation token, and time provider from <paramref name="parentContext"/>.
    /// </summary>
    public TResult ExecuteWithContext(
        KevlarContext parentContext,
        Func<KevlarContext, TResult> action)
    {
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(parentContext, nameof(parentContext));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteWithContext(parentContext, action);
        }

        return ShieldEngine.ExecuteWithParentContextSync(
            Head,
            Name,
            action,
            static (a, context) => a(context),
            parentContext);
    }

    /// <summary>
    /// Executes a context-aware delegate synchronously through the pipeline using the properties,
    /// cancellation token, and time provider from <paramref name="parentContext"/>, threading
    /// <paramref name="state"/> to avoid closure allocations.
    /// </summary>
    public TResult ExecuteWithContext<TState>(
        KevlarContext parentContext,
        TState state,
        Func<TState, KevlarContext, TResult> action)
    {
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(parentContext, nameof(parentContext));
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteWithContext(parentContext, state, action);
        }

        return ShieldEngine.ExecuteWithParentContextSync(Head, Name, state, action, parentContext);
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
        if (_currentSnapshot is { } currentSnapshot)
        {
            return currentSnapshot().ExecuteWithContext(
                state,
                initializeProperties,
                action,
                cancellationToken);
        }

        return ShieldEngine.ExecuteWithContextSync(
            Head,
            TimeOrSystem,
            Name,
            state,
            initializeProperties,
            action,
            cancellationToken);
    }

    /// <summary>
    /// Executes a context-aware delegate synchronously through the pipeline without seeding execution
    /// properties. The context is pooled and is valid only for the duration of the delegate invocation;
    /// never retain it.
    /// </summary>
    public TResult ExecuteWithContext(
        Func<KevlarContext, TResult> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ExecuteWithContext(
            action,
            static (_, _) => { },
            static (a, context) => a(context),
            cancellationToken);
    }

    /// <summary>Describes the pipeline, outermost strategy first, like <see cref="Shield.ToString"/>.</summary>
    public override string ToString()
    {
        var snapshot = CurrentSnapshot;
        return Shield.Describe(snapshot.Name, snapshot.Strategies);
    }

    // ── Internals ───────────────────────────────────────────────────────────────────────

    internal Shield<TResult> Append(Strategy strategy, OutcomeJudge? ambient = null)
    {
        var snapshot = CurrentSnapshot;
        var strategies = new Strategy[snapshot.Strategies.Length + 1];
        Array.Copy(snapshot.Strategies, strategies, snapshot.Strategies.Length);
        strategies[snapshot.Strategies.Length] = strategy;
        var shield = new Shield<TResult>(
            strategies,
            ambient ?? snapshot.Ambient,
            snapshot.Name,
            snapshot.Time,
            snapshot.AppliedDecorators);
        StrategyAppendObserver.Notify(snapshot.Strategies, strategy, snapshot.Name, shield);
        return shield;
    }

    internal Shield<TResult> MarkDecoratorApplied(
        IShieldDecorator[] appliedDecorators,
        IShieldDecorator decorator)
    {
        var snapshot = CurrentSnapshot;
        var decorators = new IShieldDecorator[appliedDecorators.Length + 1];
        Array.Copy(appliedDecorators, decorators, appliedDecorators.Length);
        decorators[^1] = decorator;
        var decorated = new Shield<TResult>(
            snapshot.Strategies,
            snapshot.Ambient,
            snapshot.Name,
            snapshot.Time,
            decorators);
        StrategyAppendObserver.NotifyComposition(snapshot.Strategies, snapshot.Name, decorated);
        return decorated;
    }
}

#pragma warning restore RS0026
