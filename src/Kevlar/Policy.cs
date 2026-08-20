using Kevlar.Internal;
using Kevlar.Strategies;

namespace Kevlar;

/// <summary>
/// An immutable, thread-safe resilience pipeline. Build one once — with the static factories and
/// fluent chaining — and reuse it for every execution. The first strategy in a chain is the
/// outermost, exactly like ASP.NET middleware: <c>Policy.Timeout(t).Retry(3)</c> applies a total
/// timeout around all retries.
/// </summary>
/// <remarks>
/// Stateful strategies (circuit breakers, rate limiters, bulkheads) live with the policy instance
/// they were created in. Composing that policy into others (via <c>Wrap</c> or <c>Compose</c>)
/// intentionally shares the state; creating a new policy creates fresh state.
/// </remarks>
public sealed class Policy
{
    internal readonly Strategy[] Strategies;
    internal readonly StrategyNode? Head;
    internal readonly OutcomeJudge? Ambient;
    internal readonly TimeProvider? Time;

    internal Policy(Strategy[] strategies, OutcomeJudge? ambient, string? name, TimeProvider? timeProvider)
    {
        Strategies = strategies;
        Head = BuildChain(strategies);
        Ambient = ambient;
        Name = name;
        Time = timeProvider;
    }

    /// <summary>The policy's diagnostic name, if assigned via <c>WithName</c>.</summary>
    public string? Name { get; }

    /// <summary>A policy with no strategies: executions pass straight through.</summary>
    public static Policy Empty { get; } = new([], null, null, null);

    internal OutcomeJudge JudgeOrDefault => Ambient ?? OutcomeJudge.Default;

    internal TimeProvider TimeOrSystem => Time ?? TimeProvider.System;

    // ── Static factories ────────────────────────────────────────────────────────────────

    /// <summary>Retries failed executions up to <paramref name="maxRetries"/> times with the default exponential jittered backoff.</summary>
    public static Policy Retry(int maxRetries = 3) => PolicyExtensions.Retry(Empty, maxRetries);

    /// <summary>Retries failed executions up to <paramref name="maxRetries"/> times with the given backoff.</summary>
    public static Policy Retry(int maxRetries, Backoff backoff) => PolicyExtensions.Retry(Empty, maxRetries, backoff);

    /// <summary>Adds a retry strategy configured via <paramref name="configure"/>.</summary>
    public static Policy Retry(Action<RetryOptions> configure) => PolicyExtensions.Retry(Empty, configure);

    /// <summary>Retries failed executions indefinitely.</summary>
    public static Policy RetryForever(Backoff? backoff = null) => PolicyExtensions.RetryForever(Empty, backoff);

    /// <summary>Cancels executions that exceed <paramref name="timeout"/>, surfacing <see cref="TimeoutExceededException"/>.</summary>
    public static Policy Timeout(TimeSpan timeout) => PolicyExtensions.Timeout(Empty, timeout);

    /// <summary>Adds a timeout strategy configured via <paramref name="configure"/>.</summary>
    public static Policy Timeout(Action<TimeoutOptions> configure) => PolicyExtensions.Timeout(Empty, configure);

    /// <summary>Breaks the circuit for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive failures.</summary>
    public static Policy CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration) =>
        PolicyExtensions.CircuitBreaker(Empty, consecutiveFailures, breakDuration);

    /// <summary>Adds a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public static Policy CircuitBreaker(Action<CircuitBreakerOptions> configure) => PolicyExtensions.CircuitBreaker(Empty, configure);

    /// <summary>Limits throughput to <paramref name="permits"/> executions per <paramref name="perWindow"/> (token bucket).</summary>
    public static Policy RateLimit(int permits, TimeSpan perWindow) => PolicyExtensions.RateLimit(Empty, permits, perWindow);

    /// <summary>Adds a rate limit strategy configured via <paramref name="configure"/>.</summary>
    public static Policy RateLimit(Action<RateLimitOptions> configure) => PolicyExtensions.RateLimit(Empty, configure);

    /// <summary>Caps concurrent executions at <paramref name="maxConcurrency"/> with an optional wait queue.</summary>
    public static Policy Bulkhead(int maxConcurrency, int maxQueue = 0) => PolicyExtensions.Bulkhead(Empty, maxConcurrency, maxQueue);

    /// <summary>Adds a bulkhead strategy configured via <paramref name="configure"/>.</summary>
    public static Policy Bulkhead(Action<BulkheadOptions> configure) => PolicyExtensions.Bulkhead(Empty, configure);

    /// <summary>Races up to <paramref name="maxAttempts"/> concurrent attempts staggered by <paramref name="delay"/>; first acceptable outcome wins.</summary>
    public static Policy Hedge(int maxAttempts, TimeSpan delay) => PolicyExtensions.Hedge(Empty, maxAttempts, delay);

    /// <summary>Adds a hedging strategy configured via <paramref name="configure"/>.</summary>
    public static Policy Hedge(Action<HedgingOptions> configure) => PolicyExtensions.Hedge(Empty, configure);

    /// <summary>Starts a pipeline with a custom <see cref="Strategy"/> implementation.</summary>
    public static Policy Use(Strategy strategy) => PolicyExtensions.Use(Empty, strategy);

    /// <summary>Starts a handling clause: subsequent reactive strategies act on exceptions of type <typeparamref name="TException"/>.</summary>
    public static PolicyBuilder Handle<TException>()
        where TException : Exception
        => PolicyExtensions.Handle<TException>(Empty);

    /// <summary>Starts a handling clause for exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>.</summary>
    public static PolicyBuilder Handle<TException>(Func<TException, bool> predicate)
        where TException : Exception
        => PolicyExtensions.Handle(Empty, predicate);

    /// <summary>Starts a handling clause for exceptions matching <paramref name="predicate"/>.</summary>
    public static PolicyBuilder HandleWhen(Func<Exception, bool> predicate) => PolicyExtensions.HandleWhen(Empty, predicate);

    /// <summary>Starts building a result-aware policy for executions returning <typeparamref name="TResult"/>.</summary>
    public static PolicyBuilder<TResult> For<TResult>() => new(Policy<TResult>.Empty);

    /// <summary>
    /// Merges policies into one pipeline. The first policy is the outermost. Stateful strategies
    /// keep their identity, so a shared circuit breaker policy shares its circuit here.
    /// </summary>
    public static Policy Compose(params Policy[] policies)
    {
        Throw.IfNull(policies, nameof(policies));

        var total = 0;
        foreach (var policy in policies)
        {
            Throw.IfNull(policy, nameof(policies));
            total += policy.Strategies.Length;
        }

        var strategies = new Strategy[total];
        var offset = 0;
        foreach (var policy in policies)
        {
            Array.Copy(policy.Strategies, 0, strategies, offset, policy.Strategies.Length);
            offset += policy.Strategies.Length;
        }

        return new Policy(strategies, null, null, null);
    }

    // ── Execution ───────────────────────────────────────────────────────────────────────

    /// <summary>Executes the delegate through the pipeline. The delegate must use the cancellation token it is handed.</summary>
    public ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return PolicyEngine.ExecuteAsync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>Executes the delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public ValueTask<T> ExecuteAsync<T, TState>(TState state, Func<TState, CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return PolicyEngine.ExecuteAsync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>Executes the void delegate through the pipeline.</summary>
    public ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return StripResult(PolicyEngine.ExecuteAsync(
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
        return StripResult(PolicyEngine.ExecuteAsync(
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
    /// Executes the delegate through the pipeline and returns the outcome instead of throwing.
    /// </summary>
    public ValueTask<Outcome<T>> ExecuteOutcomeAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return PolicyEngine.ExecuteOutcomeAsync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>
    /// Executes the delegate synchronously through the pipeline. Delays (retry backoff, rate limit
    /// waits) block the calling thread. Hedging is not supported synchronously.
    /// </summary>
    public T Execute<T>(Func<CancellationToken, T> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return PolicyEngine.ExecuteSync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>Executes the delegate synchronously, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public T Execute<T, TState>(TState state, Func<TState, CancellationToken, T> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return PolicyEngine.ExecuteSync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>Executes the void delegate synchronously through the pipeline.</summary>
    public void Execute(Action<CancellationToken> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        PolicyEngine.ExecuteSync(Head, TimeOrSystem, Name, action, static (a, token) =>
        {
            a(token);
            return Nothing.Value;
        }, cancellationToken);
    }

    /// <summary>Executes the void delegate synchronously, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public void Execute<TState>(TState state, Action<TState, CancellationToken> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        PolicyEngine.ExecuteSync(Head, TimeOrSystem, Name, (state, action), static (s, token) =>
        {
            s.action(s.state, token);
            return Nothing.Value;
        }, cancellationToken);
    }

    // ── Internals ───────────────────────────────────────────────────────────────────────

    internal Policy Append(Strategy strategy, OutcomeJudge? ambient = null)
    {
        var strategies = new Strategy[Strategies.Length + 1];
        Array.Copy(Strategies, strategies, Strategies.Length);
        strategies[Strategies.Length] = strategy;
        return new Policy(strategies, ambient ?? Ambient, Name, Time);
    }

    internal static StrategyNode? BuildChain(Strategy[] strategies)
    {
        StrategyNode? next = null;
        for (var i = strategies.Length - 1; i >= 0; i--)
        {
            next = new StrategyNode(strategies[i], next);
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
