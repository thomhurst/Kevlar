using Kevlar.Internal;
using Kevlar.Strategies;

#pragma warning disable RS0026 // Execution overload parity intentionally keeps CancellationToken optional.

namespace Kevlar;

/// <summary>
/// An immutable, thread-safe resilience pipeline. Build one once — with the static factories and
/// fluent chaining — and reuse it for every execution. The first strategy in a chain is the
/// outermost, exactly like ASP.NET middleware: <c>Shield.Timeout(t).Retry(3)</c> applies a total
/// timeout around all retries.
/// </summary>
/// <remarks>
/// Stateful strategies (circuit breakers, rate limiters, bulkheads) live with the shield instance
/// they were created in. Composing that shield into others (via <c>Wrap</c> or <c>Compose</c>)
/// intentionally shares the state; creating a new shield creates fresh state.
/// </remarks>
public sealed class Shield : IShieldLifecycle
{
    internal readonly Strategy[] Strategies;
    internal readonly StrategyNode? Head;
    internal readonly OutcomeJudge? Ambient;
    internal readonly TimeProvider? Time;
    internal readonly IShieldDecorator[] AppliedDecorators;
    private readonly bool _hasVoidFallback;
    private readonly StrategyOwnerSet _strategyOwners;

    Strategy[] IShieldLifecycle.Strategies => Strategies;

    internal Shield(
        Strategy[] strategies,
        OutcomeJudge? ambient,
        string? name,
        TimeProvider? timeProvider,
        IShieldDecorator[]? appliedDecorators = null)
    {
        ValidateChain(strategies);
        Strategies = strategies;
        _strategyOwners = GetStrategyOwners(strategies);
        Head = BuildChain(strategies, _strategyOwners);
        Ambient = ambient;
        Name = name;
        Time = timeProvider;
        AppliedDecorators = appliedDecorators ?? [];

        foreach (var strategy in strategies)
        {
            if (strategy is VoidFallbackStrategy)
            {
                _hasVoidFallback = true;
                break;
            }
        }
    }

    /// <summary>The shield's diagnostic name, if assigned via <c>WithName</c>.</summary>
    public string? Name { get; }

    /// <summary>A shield with no strategies: executions pass straight through.</summary>
    public static Shield Empty { get; } = new([], null, null, null);

    internal OutcomeJudge JudgeOrDefault => Ambient ?? OutcomeJudge.Default;

    internal TimeProvider TimeOrSystem => Time ?? TimeProvider.System;

    /// <summary>
    /// Gets whether every strategy guarantees invoking the execution continuation at most once.
    /// Custom strategies may opt in through <see cref="Strategy.InvokesContinuationAtMostOnce"/>.
    /// </summary>
    public bool InvokesContinuationAtMostOnce =>
        Strategies.All(static strategy => strategy.InvokesContinuationAtMostOnce);

    // ── Static factories ────────────────────────────────────────────────────────────────

    /// <summary>Retries failed executions up to <paramref name="maxRetries"/> times with the default exponential jittered backoff.</summary>
    /// <param name="maxRetries">
    /// The number of <em>retries</em>, not the number of attempts: <c>Retry(3)</c> makes up to 4
    /// total attempts — the initial call plus 3 retries.
    /// </param>
    public static Shield Retry(int maxRetries = 3) => ShieldExtensions.Retry(Empty, maxRetries);

    /// <summary>Retries failed executions up to <paramref name="maxRetries"/> times with the given backoff.</summary>
    /// <param name="maxRetries">
    /// The number of <em>retries</em>, not the number of attempts: <c>Retry(3)</c> makes up to 4
    /// total attempts — the initial call plus 3 retries.
    /// </param>
    /// <param name="backoff">The delay computation applied between attempts.</param>
    public static Shield Retry(int maxRetries, Backoff backoff) => ShieldExtensions.Retry(Empty, maxRetries, backoff);

    /// <summary>Adds a retry strategy configured via <paramref name="configure"/>.</summary>
    /// <remarks>
    /// <see cref="RetryOptions.MaxRetries"/> counts <em>retries</em>, not attempts:
    /// <c>MaxRetries = 3</c> makes up to 4 total attempts — the initial call plus 3 retries.
    /// </remarks>
    public static Shield Retry(Action<RetryOptions> configure) => ShieldExtensions.Retry(Empty, configure);

    /// <summary>Retries failed executions indefinitely with the default exponential jittered backoff.</summary>
    public static Shield RetryForever() => ShieldExtensions.RetryForever(Empty);

    /// <summary>Retries failed executions indefinitely with the given backoff.</summary>
    /// <param name="backoff">The delay computation applied between attempts.</param>
    public static Shield RetryForever(Backoff backoff) => ShieldExtensions.RetryForever(Empty, backoff);

    /// <summary>Cancels executions that exceed <paramref name="timeout"/>, surfacing <see cref="TimeoutExceededException"/>.</summary>
    public static Shield Timeout(TimeSpan timeout) => ShieldExtensions.Timeout(Empty, timeout);

    /// <summary>Adds a timeout strategy configured via <paramref name="configure"/>.</summary>
    public static Shield Timeout(Action<TimeoutOptions> configure) => ShieldExtensions.Timeout(Empty, configure);

    /// <summary>Breaks the circuit for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive failures.</summary>
    public static Shield CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration) =>
        ShieldExtensions.CircuitBreaker(Empty, consecutiveFailures, breakDuration);

    /// <summary>Adds a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public static Shield CircuitBreaker(Action<CircuitBreakerOptions> configure) => ShieldExtensions.CircuitBreaker(Empty, configure);

    /// <summary>Limits throughput to <paramref name="permits"/> executions per <paramref name="perWindow"/> (token bucket).</summary>
    public static Shield RateLimit(int permits, TimeSpan perWindow) => ShieldExtensions.RateLimit(Empty, permits, perWindow);

    /// <summary>Adds a rate limit strategy configured via <paramref name="configure"/>.</summary>
    public static Shield RateLimit(Action<RateLimitOptions> configure) => ShieldExtensions.RateLimit(Empty, configure);

    /// <summary>Caps concurrent executions at <paramref name="maxConcurrency"/> with an optional wait queue.</summary>
    public static Shield ConcurrencyLimit(int maxConcurrency, int queueLimit = 0) => ShieldExtensions.ConcurrencyLimit(Empty, maxConcurrency, queueLimit);

    /// <summary>Adds a concurrency limit strategy configured via <paramref name="configure"/>.</summary>
    public static Shield ConcurrencyLimit(Action<ConcurrencyLimitOptions> configure) => ShieldExtensions.ConcurrencyLimit(Empty, configure);

    /// <summary>Races the primary with up to <paramref name="maxHedgedAttempts"/> additional attempts staggered by <paramref name="delay"/>; first acceptable outcome wins.</summary>
    /// <remarks>
    /// Hedging on an untyped <see cref="Shield"/> runs the execution delegate more than once,
    /// concurrently, and only its exceptions can select a winner. The delegate must therefore be
    /// idempotent: duplicate writes, charges, or sends are otherwise observable side effects of a
    /// hedge that later loses. Prefer <see cref="For{TResult}"/>, where result clauses decide which
    /// attempt is acceptable, or confirm the action is safe to repeat.
    /// </remarks>
    public static Shield Hedge(int maxHedgedAttempts, TimeSpan delay) => ShieldExtensions.Hedge(Empty, maxHedgedAttempts, delay);

    /// <summary>Adds a hedging strategy configured via <paramref name="configure"/>.</summary>
    /// <remarks>
    /// Hedging on an untyped <see cref="Shield"/> runs the execution delegate more than once,
    /// concurrently, so the delegate must be idempotent. Prefer <see cref="For{TResult}"/>, or
    /// confirm the action is safe to repeat.
    /// </remarks>
    public static Shield Hedge(Action<HedgeOptions> configure) => ShieldExtensions.Hedge(Empty, configure);

    /// <summary>
    /// Starts a pipeline with a fallback that runs <paramref name="fallback"/> in place of handled
    /// failures, receiving the handled exception. Applies to void executions only; result-returning
    /// executions fail with a descriptive <see cref="InvalidOperationException"/> — use
    /// <c>Shield.For&lt;T&gt;().FallbackTo(…)</c> for constant values or its typed <c>Fallback(…)</c> overloads for factories.
    /// </summary>
    /// <remarks>
    /// A fallback is legitimately the outermost strategy: it recovers what everything chained
    /// inside it could not.
    /// </remarks>
    public static Shield Fallback(Func<Exception, CancellationToken, ValueTask> fallback) =>
        ShieldExtensions.Fallback(Empty, fallback);

    /// <summary>
    /// Starts a pipeline with a fallback that runs <paramref name="fallback"/> in place of handled
    /// failures and configures notifications. Applies to void executions only.
    /// </summary>
    /// <remarks>Runs <see cref="FallbackOptions.OnFallback"/>, then <see cref="FallbackOptions.OnFallbackAsync"/>, before recovery. Notification failures are reported and recovery continues.</remarks>
    public static Shield Fallback(
        Func<Exception, CancellationToken, ValueTask> fallback,
        Action<FallbackOptions> configure) =>
        ShieldExtensions.Fallback(Empty, fallback, configure);

    /// <summary>
    /// Starts a pipeline with a fallback that runs <paramref name="fallback"/> in place of handled
    /// failures. Applies to void executions only; result-returning executions fail with a
    /// descriptive <see cref="InvalidOperationException"/>. Use
    /// <c>Shield.For&lt;T&gt;().FallbackTo(…)</c> for constant values or its typed <c>Fallback(…)</c>
    /// overloads for factories.
    /// </summary>
    public static Shield Fallback(Func<CancellationToken, ValueTask> fallback) =>
        ShieldExtensions.Fallback(Empty, fallback);

    /// <summary>
    /// Starts a pipeline with a fallback that runs <paramref name="fallback"/> in place of handled
    /// failures and configures notifications. Applies to void executions only.
    /// </summary>
    /// <remarks>Runs <see cref="FallbackOptions.OnFallback"/>, then <see cref="FallbackOptions.OnFallbackAsync"/>, before recovery. Notification failures are reported and recovery continues.</remarks>
    public static Shield Fallback(
        Func<CancellationToken, ValueTask> fallback,
        Action<FallbackOptions> configure) =>
        ShieldExtensions.Fallback(Empty, fallback, configure);

    /// <summary>Starts a pipeline with a custom <see cref="Strategy"/> implementation.</summary>
    public static Shield Use(Strategy strategy) => ShieldExtensions.Use(Empty, strategy);

    /// <summary>
    /// Starts a pipeline with a custom strategy created from the default handling clause.
    /// The factory runs once when the strategy is appended.
    /// </summary>
    public static Shield Use(Func<HandlingClause, Strategy> factory) => ShieldExtensions.Use(Empty, factory);

    /// <summary>Starts a handling clause: subsequent reactive strategies act on exceptions of type <typeparamref name="TException"/>. Use <see cref="ShieldExtensions.WhenAnyError(Shield)"/> to return to default handling.</summary>
    public static ShieldBuilder When<TException>()
        where TException : Exception
        => ShieldExtensions.When<TException>(Empty);

    /// <summary>Starts a handling clause for exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>. Use <see cref="ShieldExtensions.WhenAnyError(Shield)"/> to return to default handling.</summary>
    public static ShieldBuilder When<TException>(Func<TException, bool> predicate)
        where TException : Exception
        => ShieldExtensions.When(Empty, predicate);

    /// <summary>Starts a handling clause for exceptions matching <paramref name="predicate"/>. Use <see cref="ShieldExtensions.WhenAnyError(Shield)"/> to return to default handling.</summary>
    public static ShieldBuilder When(Func<Exception, bool> predicate) => ShieldExtensions.When(Empty, predicate);

    /// <summary>Starts a handling clause using the active execution and strategy context.</summary>
    public static ShieldBuilder WhenContext(Func<HandlingEvent, bool> predicate) =>
        ShieldExtensions.WhenContext(Empty, predicate);

    /// <summary>Starts a result-aware shield for executions returning <typeparamref name="TResult"/>.</summary>
    public static Shield<TResult> For<TResult>() => Shield<TResult>.Empty;

    /// <summary>
    /// Merges shields into one pipeline. The first shield is the outermost. Stateful strategies
    /// keep their identity, so a shared circuit breaker shield shares its circuit here.
    /// The result keeps the first non-null <see cref="Name"/> and <see cref="TimeProvider"/>
    /// among the inputs. Composition seals handling clauses, so reactive strategies appended
    /// afterwards use default handling unless a new clause is declared.
    /// </summary>
    public static Shield Compose(params Shield[] shields)
    {
        Throw.IfNull(shields, nameof(shields));

        var parts = new Strategy[shields.Length][];
        string? name = null;
        TimeProvider? time = null;

        for (var i = 0; i < shields.Length; i++)
        {
            var shield = shields[i];
            Throw.IfNull(shield, nameof(shields));
            parts[i] = shield.Strategies;
            name ??= shield.Name;
            time ??= shield.Time;
        }

        return new Shield(Concat(parts), null, name, time);
    }

    // ── Execution ───────────────────────────────────────────────────────────────────────

    /// <summary>Executes the delegate through the pipeline. The delegate must use the cancellation token it is handed.</summary>
    public ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        ThrowIfVoidFallbackResultExecution();
        return ShieldEngine.ExecuteAsync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>Executes the delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public ValueTask<T> ExecuteAsync<T, TState>(TState state, Func<TState, CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        ThrowIfVoidFallbackResultExecution();
        return ShieldEngine.ExecuteAsync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>
    /// Initializes execution properties, then executes a context-aware delegate through the pipeline.
    /// The context is pooled and is valid only for the duration of the delegate invocation; never retain it.
    /// </summary>
    public ValueTask<T> ExecuteWithContextAsync<T, TState>(
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, ValueTask<T>> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        ThrowIfVoidFallbackResultExecution();
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
    public ValueTask<T> ExecuteWithContextAsync<T, TState>(
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, ValueTask<T>> action,
        Action<TState, KevlarProperties> onCompleted,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(onCompleted, nameof(onCompleted));
        ThrowIfVoidFallbackResultExecution();
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
    public ValueTask<T> ExecuteWithContextAsync<T>(
        Func<KevlarContext, ValueTask<T>> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ExecuteWithContextAsync(
            action,
            static (_, _) => { },
            static (a, context) => a(context),
            cancellationToken);
    }

    /// <summary>Executes the void delegate through the pipeline.</summary>
    public ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return StripResult(ShieldEngine.ExecuteAsync(
            Head,
            TimeOrSystem,
            Name,
            action,
            static async (a, token) =>
            {
                await a(token).ConfigureAwait(false);
                return Nothing.Value;
            },
            cancellationToken));
    }

    /// <summary>Executes the void delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public ValueTask ExecuteAsync<TState>(TState state, Func<TState, CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return StripResult(ShieldEngine.ExecuteAsync(
            Head,
            TimeOrSystem,
            Name,
            (state, action),
            static async (s, token) =>
            {
                await s.action(s.state, token).ConfigureAwait(false);
                return Nothing.Value;
            },
            cancellationToken));
    }

    /// <summary>
    /// Initializes execution properties, then executes a context-aware void delegate through the pipeline.
    /// The context is pooled and is valid only for the duration of the delegate invocation; never retain it.
    /// </summary>
    public ValueTask ExecuteWithContextAsync<TState>(
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, ValueTask> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        return StripResult(ShieldEngine.ExecuteWithContextAsync(
            Head,
            TimeOrSystem,
            Name,
            (state, initializeProperties, action),
            static (s, properties) => s.initializeProperties(s.state, properties),
            static async (s, context) =>
            {
                await s.action(s.state, context).ConfigureAwait(false);
                return Nothing.Value;
            },
            cancellationToken));
    }

    /// <summary>
    /// Initializes execution properties, executes a context-aware void delegate, then exposes the
    /// final properties to <paramref name="onCompleted"/> before the pooled context is returned.
    /// </summary>
    public ValueTask ExecuteWithContextAsync<TState>(
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, ValueTask> action,
        Action<TState, KevlarProperties> onCompleted,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(onCompleted, nameof(onCompleted));
        return StripResult(ShieldEngine.ExecuteWithContextAsync(
            Head,
            TimeOrSystem,
            Name,
            (state, initializeProperties, action, onCompleted),
            static (s, properties) => s.initializeProperties(s.state, properties),
            static async (s, context) =>
            {
                await s.action(s.state, context).ConfigureAwait(false);
                return Nothing.Value;
            },
            static (s, properties) => s.onCompleted(s.state, properties),
            cancellationToken));
    }

    /// <summary>
    /// Executes a context-aware void delegate through the pipeline without seeding execution properties.
    /// The context is pooled and is valid only for the duration of the delegate invocation; never retain it.
    /// </summary>
    public ValueTask ExecuteWithContextAsync(
        Func<KevlarContext, ValueTask> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ExecuteWithContextAsync(
            action,
            static (_, _) => { },
            static (a, context) => a(context),
            cancellationToken);
    }

    /// <summary>
    /// Executes the delegate through the pipeline and returns the outcome instead of throwing.
    /// </summary>
    public ValueTask<Outcome<T>> ExecuteOutcomeAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        ThrowIfVoidFallbackResultExecution();
        return ShieldEngine.ExecuteOutcomeAsync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>
    /// Executes the delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations,
    /// and returns the outcome instead of throwing.
    /// </summary>
    public ValueTask<Outcome<T>> ExecuteOutcomeAsync<T, TState>(TState state, Func<TState, CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        ThrowIfVoidFallbackResultExecution();
        return ShieldEngine.ExecuteOutcomeAsync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>Executes a void delegate and returns success or the captured exception.</summary>
    public ValueTask<Outcome> ExecuteOutcomeAsync(
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return StripOutcome(ShieldEngine.ExecuteOutcomeAsync(
            Head,
            TimeOrSystem,
            Name,
            action,
            static async (a, token) =>
            {
                await a(token).ConfigureAwait(false);
                return Nothing.Value;
            },
            cancellationToken));
    }

    /// <summary>
    /// Executes a void delegate, threading <paramref name="state"/> to avoid closure allocations,
    /// and returns success or the captured exception.
    /// </summary>
    public ValueTask<Outcome> ExecuteOutcomeAsync<TState>(
        TState state,
        Func<TState, CancellationToken, ValueTask> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return StripOutcome(ShieldEngine.ExecuteOutcomeAsync(
            Head,
            TimeOrSystem,
            Name,
            (state, action),
            static async (s, token) =>
            {
                await s.action(s.state, token).ConfigureAwait(false);
                return Nothing.Value;
            },
            cancellationToken));
    }

    /// <summary>Executes a result-returning delegate synchronously and returns its outcome.</summary>
    public Outcome<T> ExecuteOutcome<T>(
        Func<CancellationToken, T> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        ThrowIfVoidFallbackResultExecution();
        return ShieldEngine.ExecuteOutcomeSync(
            Head,
            TimeOrSystem,
            Name,
            action,
            static (a, token) => a(token),
            cancellationToken);
    }

    /// <summary>
    /// Executes a result-returning delegate synchronously, threading <paramref name="state"/> to
    /// avoid closure allocations, and returns its outcome.
    /// </summary>
    public Outcome<T> ExecuteOutcome<T, TState>(
        TState state,
        Func<TState, CancellationToken, T> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        ThrowIfVoidFallbackResultExecution();
        return ShieldEngine.ExecuteOutcomeSync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>Executes a void delegate synchronously and returns success or the captured exception.</summary>
    public Outcome ExecuteOutcome(
        Action<CancellationToken> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteOutcomeSync(Head, TimeOrSystem, Name, action, static (a, token) =>
        {
            a(token);
            return Nothing.Value;
        }, cancellationToken);
    }

    /// <summary>
    /// Executes a void delegate synchronously, threading <paramref name="state"/> to avoid closure
    /// allocations, and returns success or the captured exception.
    /// </summary>
    public Outcome ExecuteOutcome<TState>(
        TState state,
        Action<TState, CancellationToken> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteOutcomeSync(Head, TimeOrSystem, Name, (state, action), static (s, token) =>
        {
            s.action(s.state, token);
            return Nothing.Value;
        }, cancellationToken);
    }

    /// <summary>
    /// Executes the delegate synchronously through the pipeline. Delays (retry backoff, rate limit
    /// waits) block the calling thread. Hedging is not supported synchronously.
    /// </summary>
    public T Execute<T>(Func<CancellationToken, T> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        ThrowIfVoidFallbackResultExecution();
        return ShieldEngine.ExecuteSync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>Executes the delegate synchronously, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public T Execute<T, TState>(TState state, Func<TState, CancellationToken, T> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        ThrowIfVoidFallbackResultExecution();
        return ShieldEngine.ExecuteSync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>
    /// Initializes execution properties, then executes a context-aware delegate synchronously through the pipeline.
    /// The context is pooled and is valid only for the duration of the delegate invocation; never retain it.
    /// </summary>
    public T ExecuteWithContext<T, TState>(
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, T> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        ThrowIfVoidFallbackResultExecution();
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
    public T ExecuteWithContext<T>(Func<KevlarContext, T> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ExecuteWithContext(
            action,
            static (_, _) => { },
            static (a, context) => a(context),
            cancellationToken);
    }

    /// <summary>Executes the void delegate synchronously through the pipeline.</summary>
    public void Execute(Action<CancellationToken> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        ShieldEngine.ExecuteSync(Head, TimeOrSystem, Name, action, static (a, token) =>
        {
            a(token);
            return Nothing.Value;
        }, cancellationToken);
    }

    /// <summary>Executes the void delegate synchronously, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public void Execute<TState>(TState state, Action<TState, CancellationToken> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        ShieldEngine.ExecuteSync(Head, TimeOrSystem, Name, (state, action), static (s, token) =>
        {
            s.action(s.state, token);
            return Nothing.Value;
        }, cancellationToken);
    }

    /// <summary>
    /// Initializes execution properties, then executes a context-aware void delegate synchronously through the pipeline.
    /// The context is pooled and is valid only for the duration of the delegate invocation; never retain it.
    /// </summary>
    public void ExecuteWithContext<TState>(
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Action<TState, KevlarContext> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        ShieldEngine.ExecuteWithContextSync(
            Head,
            TimeOrSystem,
            Name,
            (state, initializeProperties, action),
            static (s, properties) => s.initializeProperties(s.state, properties),
            static (s, context) =>
            {
                s.action(s.state, context);
                return Nothing.Value;
            },
            cancellationToken);
    }

    /// <summary>
    /// Executes a context-aware void delegate synchronously through the pipeline without seeding
    /// execution properties. The context is pooled and is valid only for the duration of the delegate
    /// invocation; never retain it.
    /// </summary>
    public void ExecuteWithContext(Action<KevlarContext> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        ExecuteWithContext(
            action,
            static (_, _) => { },
            static (a, context) => a(context),
            cancellationToken);
    }

    /// <summary>
    /// Describes the pipeline, outermost strategy first, e.g.
    /// <c>Timeout(30s) → Retry(3, exponential 250ms ×2, equal jitter, cap 30s) → CircuitBreaker(5 consecutive, break 30s)</c>.
    /// </summary>
    public override string ToString() => Describe(Name, Strategies);

    // ── Internals ───────────────────────────────────────────────────────────────────────

    private void ThrowIfVoidFallbackResultExecution()
    {
        if (_hasVoidFallback)
        {
            throw CreateVoidFallbackResultException();
        }
    }

    internal static InvalidOperationException CreateVoidFallbackResultException() =>
        new(
            "Fallback on a non-generic Shield applies only to void executions. " +
            "For executions that return a value, build a result-aware shield with " +
            "Shield.For<T>() and use its Fallback overloads.");

    internal static string Describe(string? name, Strategy[] strategies)
    {
        var visibleCount = strategies.Count(static strategy => strategy is not ITransparentStrategy);
        var pipeline = visibleCount == 0 ? "(empty)" : DescribeStrategies(strategies, visibleCount);

        return name is null ? pipeline : $"{name}: {pipeline}";
    }

    /// <summary>
    /// Renders the chain, prefixing each run of strategies that shares a non-default handling
    /// clause with <c>[when …]</c> and marking strategies whose options replaced that clause
    /// locally. Proactive strategies carry no clause and never open or close a run.
    /// </summary>
    private static string DescribeStrategies(Strategy[] strategies, int visibleCount)
    {
        var parts = new string[visibleCount];
        string? activeClause = null;
        var partIndex = 0;

        for (var i = 0; i < strategies.Length; i++)
        {
            var strategy = strategies[i];
            if (strategy is ITransparentStrategy)
            {
                continue;
            }

            var description = strategy.Describe();

            if (strategy.HasHandlingOverride)
            {
                parts[partIndex++] = description + " (local handling)";
                continue;
            }

            if (strategy.ReactiveJudge is not { } judge)
            {
                parts[partIndex++] = description;
                continue;
            }

            var clause = judge.Description;
            if (clause is null)
            {
                activeClause = null;
                parts[partIndex++] = description;
                continue;
            }

            parts[partIndex++] = string.Equals(clause, activeClause, StringComparison.Ordinal)
                ? description
                : $"[when {clause}] {description}";
            activeClause = clause;
        }

        return string.Join(" → ", parts);
    }

    internal static void ValidateChain(Strategy[] strategies)
    {
        for (var i = 1; i < strategies.Length; i++)
        {
            for (var j = 0; j < i; j++)
            {
                if (ReferenceEquals(strategies[i], strategies[j]) && strategies[i].IsDuplicateReferenceUnsafe)
                {
                    throw new InvalidOperationException(
                        $"This chain contains the same strategy instance ({strategies[i].Describe()}) " +
                        $"at positions {j + 1} and {i + 1}. Reusing one stateful instance inside a " +
                        "single chain can deadlock or double-count. Create independent strategy " +
                        "instances, or share the instance across separate shields instead.");
                }
            }
        }

        // A fallback that shares its handling clause with an outer retry, hedge or breaker
        // swallows the failures that strategy exists to see, making it unreachable.
        for (var i = 1; i < strategies.Length; i++)
        {
            if (!strategies[i].IsFallback || strategies[i].ReactiveJudge is not { } fallbackJudge)
            {
                continue;
            }

            for (var j = 0; j < i; j++)
            {
                var outer = strategies[j];
                if (!outer.IsFallback && ReferenceEquals(outer.ReactiveJudge, fallbackJudge))
                {
                    throw new InvalidOperationException(
                        $"This chain places Fallback inside {outer.Describe()} with the same handling clause, " +
                        $"so the fallback recovers every failure before {outer.Describe()} can see one — " +
                        "making it unreachable. Chain the Fallback first (the first strategy is the outermost), " +
                        "or give the fallback its own narrower handling clause.");
                }
            }
        }
    }

    internal Shield Append(Strategy strategy, OutcomeJudge? ambient = null)
    {
        var strategies = new Strategy[Strategies.Length + 1];
        Array.Copy(Strategies, strategies, Strategies.Length);
        strategies[Strategies.Length] = strategy;
        var shield = new Shield(strategies, ambient ?? Ambient, Name, Time, AppliedDecorators);
        StrategyAppendObserver.Notify(Strategies, strategy, Name);
        return shield;
    }

    internal Shield MarkDecoratorApplied(
        IShieldDecorator[] appliedDecorators,
        IShieldDecorator decorator)
    {
        var decorators = new IShieldDecorator[appliedDecorators.Length + 1];
        Array.Copy(appliedDecorators, decorators, appliedDecorators.Length);
        decorators[^1] = decorator;
        return new Shield(Strategies, Ambient, Name, Time, decorators);
    }

    internal static StrategyNode? BuildChain(Strategy[] strategies, StrategyOwnerSet shieldOwners)
    {
        var indexes = new int[strategies.Length];
        var previousIndexes = new int[strategies.Length];
        var visibleIndex = -1;
        for (var i = 0; i < strategies.Length; i++)
        {
            previousIndexes[i] = visibleIndex;
            if (strategies[i] is not ITransparentStrategy)
            {
                visibleIndex++;
            }

            indexes[i] = visibleIndex;
        }

        StrategyNode? next = null;
        var shieldOwnerReference = new WeakReference<StrategyOwnerSet>(shieldOwners);
        for (var i = strategies.Length - 1; i >= 0; i--)
        {
            next = new StrategyNode(
                strategies[i],
                next,
                indexes[i],
                previousIndexes[i],
                i > 0 && strategies[i - 1].RequiresContinuationOverlapIsolation,
                shieldOwnerReference);
        }

        return next;
    }

    internal static StrategyOwnerSet GetStrategyOwners(Strategy[] strategies)
    {
        if (strategies.Length == 0)
        {
            return new StrategyOwnerSet([]);
        }

        var owners = new object[strategies.Length];
        for (var index = 0; index < strategies.Length; index++)
        {
            owners[index] = strategies[index].GetShieldOwner();
        }

        return new StrategyOwnerSet(owners);
    }

    internal static Strategy[] Concat(Strategy[] first, Strategy[] second)
    {
        if (first.Length == 0)
        {
            return second;
        }

        if (second.Length == 0)
        {
            return first;
        }

        var strategies = new Strategy[first.Length + second.Length];
        Array.Copy(first, strategies, first.Length);
        Array.Copy(second, 0, strategies, first.Length, second.Length);
        return strategies;
    }

    /// <summary>Flattens the strategy arrays of composed shields, first shield outermost.</summary>
    internal static Strategy[] Concat(Strategy[][] parts)
    {
        var total = 0;
        foreach (var part in parts)
        {
            total += part.Length;
        }

        var strategies = new Strategy[total];
        var offset = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, strategies, offset, part.Length);
            offset += part.Length;
        }

        return strategies;
    }

    private static ValueTask StripResult(ValueTask<Nothing> pipeline)
    {
        if (pipeline.IsCompletedSuccessfully)
        {
            _ = pipeline.Result;
            return default;
        }

        return new ValueTask(pipeline.AsTask());
    }

    private static ValueTask<Outcome> StripOutcome(ValueTask<Outcome<Nothing>> pipeline)
    {
        if (pipeline.IsCompletedSuccessfully)
        {
            return new ValueTask<Outcome>(pipeline.Result);
        }

        return AwaitOutcome(pipeline);
    }

    private static async ValueTask<Outcome> AwaitOutcome(ValueTask<Outcome<Nothing>> pipeline) =>
        await pipeline.ConfigureAwait(false);
}

#pragma warning restore RS0026
