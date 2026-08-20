using Kevlar.Internal;
using Kevlar.Strategies;

namespace Kevlar;

/// <summary>
/// An immutable, thread-safe, result-aware resilience pipeline for executions returning
/// <typeparamref name="TResult"/>. Unlike <see cref="Policy"/>, its handling clauses can react to
/// result values (<c>HandleResult</c>) as well as exceptions. The first strategy in a chain is the
/// outermost.
/// </summary>
public sealed class Policy<TResult>
{
    internal readonly Strategy[] Strategies;
    internal readonly StrategyNode? Head;
    internal readonly OutcomeJudge? Ambient;
    internal readonly TimeProvider? Time;

    internal Policy(Strategy[] strategies, OutcomeJudge? ambient, string? name, TimeProvider? timeProvider)
    {
        Strategies = strategies;
        Head = Policy.BuildChain(strategies);
        Ambient = ambient;
        Name = name;
        Time = timeProvider;
    }

    /// <summary>The policy's diagnostic name, if assigned via <see cref="WithName"/>.</summary>
    public string? Name { get; }

    internal static Policy<TResult> Empty { get; } = new([], null, null, null);

    internal OutcomeJudge JudgeOrDefault => Ambient ?? OutcomeJudge.Default;

    internal TimeProvider TimeOrSystem => Time ?? TimeProvider.System;

    // ── Handling clauses ────────────────────────────────────────────────────────────────

    /// <summary>Starts a handling clause: subsequent reactive strategies act on exceptions of type <typeparamref name="TException"/>.</summary>
    public PolicyBuilder<TResult> Handle<TException>()
        where TException : Exception
        => new PolicyBuilder<TResult>(this).Handle<TException>();

    /// <summary>Starts a handling clause for exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>.</summary>
    public PolicyBuilder<TResult> Handle<TException>(Func<TException, bool> predicate)
        where TException : Exception
        => new PolicyBuilder<TResult>(this).Handle(predicate);

    /// <summary>Starts a handling clause for exceptions matching <paramref name="predicate"/>.</summary>
    public PolicyBuilder<TResult> HandleWhen(Func<Exception, bool> predicate) => new PolicyBuilder<TResult>(this).HandleWhen(predicate);

    /// <summary>Starts a handling clause for results matching <paramref name="predicate"/>.</summary>
    public PolicyBuilder<TResult> HandleResult(Func<TResult, bool> predicate) => new PolicyBuilder<TResult>(this).HandleResult(predicate);

    /// <summary>Starts a handling clause for results equal to <paramref name="result"/>.</summary>
    public PolicyBuilder<TResult> HandleResult(TResult result) => new PolicyBuilder<TResult>(this).HandleResult(result);

    // ── Strategy chaining ───────────────────────────────────────────────────────────────

    /// <summary>Retries handled outcomes up to <paramref name="maxRetries"/> times with the default exponential jittered backoff.</summary>
    public Policy<TResult> Retry(int maxRetries = 3) => Retry(options => options.MaxRetries = maxRetries);

    /// <summary>Retries handled outcomes up to <paramref name="maxRetries"/> times with the given backoff.</summary>
    public Policy<TResult> Retry(int maxRetries, Backoff backoff) => Retry(options =>
    {
        options.MaxRetries = maxRetries;
        options.Backoff = backoff;
    });

    /// <summary>Adds a retry strategy configured via <paramref name="configure"/>.</summary>
    public Policy<TResult> Retry(Action<RetryOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        var options = new RetryOptions();
        configure(options);
        return Append(new RetryStrategy(options, JudgeOrDefault));
    }

    /// <summary>Retries handled outcomes indefinitely.</summary>
    public Policy<TResult> RetryForever(Backoff? backoff = null) => Retry(options =>
    {
        options.MaxRetries = int.MaxValue;
        options.Backoff = backoff ?? Backoff.Default;
    });

    /// <summary>Cancels executions that exceed <paramref name="timeout"/>, surfacing <see cref="TimeoutExceededException"/>.</summary>
    public Policy<TResult> Timeout(TimeSpan timeout) => Timeout(options => options.Timeout = timeout);

    /// <summary>Adds a timeout strategy configured via <paramref name="configure"/>.</summary>
    public Policy<TResult> Timeout(Action<TimeoutOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        var options = new TimeoutOptions();
        configure(options);
        return Append(new TimeoutStrategy(options));
    }

    /// <summary>Breaks the circuit for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive handled outcomes.</summary>
    public Policy<TResult> CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration) => CircuitBreaker(options =>
    {
        options.ConsecutiveFailures = consecutiveFailures;
        options.BreakDuration = breakDuration;
    });

    /// <summary>Adds a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public Policy<TResult> CircuitBreaker(Action<CircuitBreakerOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        var options = new CircuitBreakerOptions();
        configure(options);
        return Append(new CircuitBreakerStrategy(options, JudgeOrDefault));
    }

    /// <summary>Limits throughput to <paramref name="permits"/> executions per <paramref name="perWindow"/> (token bucket).</summary>
    public Policy<TResult> RateLimit(int permits, TimeSpan perWindow) => RateLimit(options =>
    {
        options.Permits = permits;
        options.Window = perWindow;
    });

    /// <summary>Adds a rate limit strategy configured via <paramref name="configure"/>.</summary>
    public Policy<TResult> RateLimit(Action<RateLimitOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        var options = new RateLimitOptions();
        configure(options);
        return Append(new RateLimitStrategy(options));
    }

    /// <summary>Caps concurrent executions at <paramref name="maxConcurrency"/> with an optional wait queue.</summary>
    public Policy<TResult> Bulkhead(int maxConcurrency, int maxQueue = 0) => Bulkhead(options =>
    {
        options.MaxConcurrency = maxConcurrency;
        options.MaxQueue = maxQueue;
    });

    /// <summary>Adds a bulkhead strategy configured via <paramref name="configure"/>.</summary>
    public Policy<TResult> Bulkhead(Action<BulkheadOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        var options = new BulkheadOptions();
        configure(options);
        return Append(new BulkheadStrategy(options));
    }

    /// <summary>Races up to <paramref name="maxAttempts"/> concurrent attempts staggered by <paramref name="delay"/>; first acceptable outcome wins.</summary>
    public Policy<TResult> Hedge(int maxAttempts, TimeSpan delay) => Hedge(options =>
    {
        options.MaxAttempts = maxAttempts;
        options.Delay = delay;
    });

    /// <summary>Adds a hedging strategy configured via <paramref name="configure"/>.</summary>
    public Policy<TResult> Hedge(Action<HedgingOptions> configure)
    {
        Throw.IfNull(configure, nameof(configure));
        var options = new HedgingOptions();
        configure(options);
        return Append(new HedgingStrategy(options, JudgeOrDefault));
    }

    /// <summary>Replaces handled outcomes with <paramref name="fallbackValue"/>.</summary>
    public Policy<TResult> Fallback(TResult fallbackValue, Action<FallbackEvent>? onFallback = null) =>
        Append(new FallbackStrategy<TResult>((_, _) => new ValueTask<TResult>(fallbackValue), JudgeOrDefault, onFallback));

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/>.</summary>
    public Policy<TResult> Fallback(Func<CancellationToken, ValueTask<TResult>> fallback, Action<FallbackEvent>? onFallback = null)
    {
        Throw.IfNull(fallback, nameof(fallback));
        return Append(new FallbackStrategy<TResult>((_, context) => fallback(context.CancellationToken), JudgeOrDefault, onFallback));
    }

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/>, which receives the handled outcome.</summary>
    public Policy<TResult> Fallback(Func<Outcome<TResult>, CancellationToken, ValueTask<TResult>> fallback, Action<FallbackEvent>? onFallback = null)
    {
        Throw.IfNull(fallback, nameof(fallback));
        return Append(new FallbackStrategy<TResult>((outcome, context) => fallback(outcome, context.CancellationToken), JudgeOrDefault, onFallback));
    }

    /// <summary>Appends a custom <see cref="Strategy"/> implementation to the pipeline.</summary>
    public Policy<TResult> Use(Strategy strategy)
    {
        Throw.IfNull(strategy, nameof(strategy));
        return Append(strategy);
    }

    // ── Composition ─────────────────────────────────────────────────────────────────────

    /// <summary>Wraps <paramref name="inner"/> inside this policy: this policy's strategies run outermost.</summary>
    public Policy<TResult> Wrap(Policy inner)
    {
        Throw.IfNull(inner, nameof(inner));
        return new Policy<TResult>(Policy.Concat(Strategies, inner.Strategies), Ambient, Name, Time);
    }

    /// <summary>Wraps <paramref name="inner"/> inside this policy: this policy's strategies run outermost.</summary>
    public Policy<TResult> Wrap(Policy<TResult> inner)
    {
        Throw.IfNull(inner, nameof(inner));
        return new Policy<TResult>(Policy.Concat(Strategies, inner.Strategies), Ambient ?? inner.Ambient, Name, Time);
    }

    /// <summary>Returns a copy of this policy with a diagnostic name (surfaced as <see cref="KevlarContext.PolicyName"/>).</summary>
    public Policy<TResult> WithName(string name)
    {
        Throw.IfNull(name, nameof(name));
        return new Policy<TResult>(Strategies, Ambient, name, Time);
    }

    /// <summary>Returns a copy of this policy using the given <see cref="TimeProvider"/> for delays, timeouts and time windows.</summary>
    public Policy<TResult> WithTimeProvider(TimeProvider timeProvider)
    {
        Throw.IfNull(timeProvider, nameof(timeProvider));
        return new Policy<TResult>(Strategies, Ambient, Name, timeProvider);
    }

    // ── Execution ───────────────────────────────────────────────────────────────────────

    /// <summary>Executes the delegate through the pipeline. The delegate must use the cancellation token it is handed.</summary>
    public ValueTask<TResult> ExecuteAsync(Func<CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return PolicyEngine.ExecuteAsync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>Executes the delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public ValueTask<TResult> ExecuteAsync<TState>(TState state, Func<TState, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return PolicyEngine.ExecuteAsync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    /// <summary>Executes the delegate through the pipeline and returns the outcome instead of throwing.</summary>
    public ValueTask<Outcome<TResult>> ExecuteOutcomeAsync(Func<CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return PolicyEngine.ExecuteOutcomeAsync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>
    /// Executes the delegate synchronously through the pipeline. Delays block the calling thread.
    /// Hedging is not supported synchronously.
    /// </summary>
    public TResult Execute(Func<CancellationToken, TResult> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return PolicyEngine.ExecuteSync(Head, TimeOrSystem, Name, action, static (a, token) => a(token), cancellationToken);
    }

    /// <summary>Executes the delegate synchronously, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public TResult Execute<TState>(TState state, Func<TState, CancellationToken, TResult> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(action, nameof(action));
        return PolicyEngine.ExecuteSync(Head, TimeOrSystem, Name, state, action, cancellationToken);
    }

    // ── Internals ───────────────────────────────────────────────────────────────────────

    internal Policy<TResult> Append(Strategy strategy, OutcomeJudge? ambient = null)
    {
        var strategies = new Strategy[Strategies.Length + 1];
        Array.Copy(Strategies, strategies, Strategies.Length);
        strategies[Strategies.Length] = strategy;
        return new Policy<TResult>(strategies, ambient ?? Ambient, Name, Time);
    }
}
