using Kevlar.Internal;
using Kevlar.Strategies;

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
public sealed class Shield
{
    internal readonly Strategy[] Strategies;
    internal readonly StrategyNode? Head;
    internal readonly OutcomeJudge? Ambient;
    internal readonly TimeProvider? Time;

    internal Shield(Strategy[] strategies, OutcomeJudge? ambient, string? name, TimeProvider? timeProvider)
    {
        ValidateChain(strategies);
        Strategies = strategies;
        Head = BuildChain(strategies);
        Ambient = ambient;
        Name = name;
        Time = timeProvider;
    }

    /// <summary>The shield's diagnostic name, if assigned via <c>WithName</c>.</summary>
    public string? Name { get; }

    /// <summary>A shield with no strategies: executions pass straight through.</summary>
    public static Shield Empty { get; } = new([], null, null, null);

    internal OutcomeJudge JudgeOrDefault => Ambient ?? OutcomeJudge.Default;

    internal TimeProvider TimeOrSystem => Time ?? TimeProvider.System;

    /// <summary>
    /// Gets whether every strategy guarantees invoking the execution continuation at most once.
    /// Custom strategies are treated conservatively as potentially multi-attempt.
    /// </summary>
    public bool InvokesContinuationAtMostOnce =>
        Strategies.All(static strategy => strategy.InvokesContinuationAtMostOnce);

    // ── Static factories ────────────────────────────────────────────────────────────────

    /// <summary>Retries failed executions up to <paramref name="maxRetries"/> times with the default exponential jittered backoff.</summary>
    public static Shield Retry(int maxRetries = 3) => ShieldExtensions.Retry(Empty, maxRetries);

    /// <summary>Retries failed executions up to <paramref name="maxRetries"/> times with the given backoff.</summary>
    public static Shield Retry(int maxRetries, Backoff backoff) => ShieldExtensions.Retry(Empty, maxRetries, backoff);

    /// <summary>Adds a retry strategy configured via <paramref name="configure"/>.</summary>
    public static Shield Retry(Action<RetryOptions> configure) => ShieldExtensions.Retry(Empty, configure);

    /// <summary>Retries failed executions indefinitely.</summary>
    public static Shield RetryForever(Backoff? backoff = null) => ShieldExtensions.RetryForever(Empty, backoff);

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
    public static Shield ConcurrencyLimit(int maxConcurrency, int maxQueue = 0) => ShieldExtensions.ConcurrencyLimit(Empty, maxConcurrency, maxQueue);

    /// <summary>Adds a concurrency limit strategy configured via <paramref name="configure"/>.</summary>
    public static Shield ConcurrencyLimit(Action<ConcurrencyLimitOptions> configure) => ShieldExtensions.ConcurrencyLimit(Empty, configure);

    /// <summary>Races up to <paramref name="maxAttempts"/> concurrent attempts staggered by <paramref name="delay"/>; first acceptable outcome wins.</summary>
    public static Shield Hedge(int maxAttempts, TimeSpan delay) => ShieldExtensions.Hedge(Empty, maxAttempts, delay);

    /// <summary>Adds a hedging strategy configured via <paramref name="configure"/>.</summary>
    public static Shield Hedge(Action<HedgingOptions> configure) => ShieldExtensions.Hedge(Empty, configure);

    /// <summary>Starts a pipeline with a custom <see cref="Strategy"/> implementation.</summary>
    public static Shield Use(Strategy strategy) => ShieldExtensions.Use(Empty, strategy);

    /// <summary>
    /// Starts a pipeline with a custom strategy created from the default handling clause.
    /// The factory runs once when the strategy is appended.
    /// </summary>
    public static Shield Use(Func<HandlingClause, Strategy> factory) => ShieldExtensions.Use(Empty, factory);

    /// <summary>Starts a handling clause: subsequent reactive strategies act on exceptions of type <typeparamref name="TException"/>.</summary>
    public static ShieldBuilder When<TException>()
        where TException : Exception
        => ShieldExtensions.When<TException>(Empty);

    /// <summary>Starts a handling clause for exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>.</summary>
    public static ShieldBuilder When<TException>(Func<TException, bool> predicate)
        where TException : Exception
        => ShieldExtensions.When(Empty, predicate);

    /// <summary>Starts a handling clause for exceptions matching <paramref name="predicate"/>.</summary>
    public static ShieldBuilder When(Func<Exception, bool> predicate) => ShieldExtensions.When(Empty, predicate);

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

        var total = 0;
        string? name = null;
        TimeProvider? time = null;

        foreach (var shield in shields)
        {
            Throw.IfNull(shield, nameof(shields));
            total += shield.Strategies.Length;
            name ??= shield.Name;
            time ??= shield.Time;
        }

        var strategies = new Strategy[total];
        var offset = 0;
        foreach (var shield in shields)
        {
            Array.Copy(shield.Strategies, 0, strategies, offset, shield.Strategies.Length);
            offset += shield.Strategies.Length;
        }

        return new Shield(strategies, null, name, time);
    }

    // ── Execution ───────────────────────────────────────────────────────────────────────

    /// <summary>Executes the delegate through the pipeline. The delegate must use the cancellation token it is handed.</summary>
    public ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteAsync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>Executes the delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public ValueTask<T> ExecuteAsync<T, TState>(TState state, Func<TState, CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
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
        return ShieldEngine.ExecuteWithContextAsync(
            Head,
            TimeOrSystem,
            Name,
            state,
            initializeProperties,
            action,
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
    /// Executes the delegate through the pipeline and returns the outcome instead of throwing.
    /// </summary>
    public ValueTask<Outcome<T>> ExecuteOutcomeAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteOutcomeAsync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>
    /// Executes the delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations,
    /// and returns the outcome instead of throwing.
    /// </summary>
    public ValueTask<Outcome<T>> ExecuteOutcomeAsync<T, TState>(TState state, Func<TState, CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteOutcomeAsync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>
    /// Executes the delegate synchronously through the pipeline. Delays (retry backoff, rate limit
    /// waits) block the calling thread. Hedging is not supported synchronously.
    /// </summary>
    public T Execute<T>(Func<CancellationToken, T> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return ShieldEngine.ExecuteSync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>Executes the delegate synchronously, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public T Execute<T, TState>(TState state, Func<TState, CancellationToken, T> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
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
        return ShieldEngine.ExecuteWithContextSync(
            Head,
            TimeOrSystem,
            Name,
            state,
            initializeProperties,
            action,
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
    /// Describes the pipeline, outermost strategy first, e.g.
    /// <c>Timeout(30s) → Retry(3, exponential 250ms ×2 +jitter ≤30s) → CircuitBreaker(5 consecutive, break 30s)</c>.
    /// </summary>
    public override string ToString() => Describe(Name, Strategies);

    // ── Internals ───────────────────────────────────────────────────────────────────────

    internal static string Describe(string? name, Strategy[] strategies)
    {
        var pipeline = strategies.Length == 0
            ? "(empty)"
            : string.Join(" → ", strategies.Select(static strategy => strategy.Describe()));

        return name is null ? pipeline : $"{name}: {pipeline}";
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
        return new Shield(strategies, ambient ?? Ambient, Name, Time);
    }

    internal static StrategyNode? BuildChain(Strategy[] strategies)
    {
        StrategyNode? next = null;
        for (var i = strategies.Length - 1; i >= 0; i--)
        {
            next = new StrategyNode(strategies[i], next, i);
        }

        return next;
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

    private static ValueTask StripResult(ValueTask<Nothing> pipeline)
    {
        if (pipeline.IsCompletedSuccessfully)
        {
            _ = pipeline.Result;
            return default;
        }

        return new ValueTask(pipeline.AsTask());
    }
}
