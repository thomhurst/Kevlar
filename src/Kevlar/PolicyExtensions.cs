using Kevlar.Internal;
using Kevlar.Strategies;

namespace Kevlar;

/// <summary>
/// Fluent chaining for <see cref="Policy"/>. Each call returns a new immutable policy with the
/// strategy appended; the earliest strategy in a chain is the outermost at execution time.
/// </summary>
public static class PolicyExtensions
{
    /// <summary>Appends a retry of handled exceptions, up to <paramref name="maxRetries"/> times, with the default exponential jittered backoff.</summary>
    public static Policy Retry(this Policy policy, int maxRetries = 3) => policy.Retry(options => options.MaxRetries = maxRetries);

    /// <summary>Appends a retry of handled exceptions, up to <paramref name="maxRetries"/> times, with the given backoff.</summary>
    public static Policy Retry(this Policy policy, int maxRetries, Backoff backoff) => policy.Retry(options =>
    {
        options.MaxRetries = maxRetries;
        options.Backoff = backoff;
    });

    /// <summary>Appends a retry strategy configured via <paramref name="configure"/>.</summary>
    public static Policy Retry(this Policy policy, Action<RetryOptions> configure)
    {
        Throw.IfNull(policy, nameof(policy));
        Throw.IfNull(configure, nameof(configure));
        var options = new RetryOptions();
        configure(options);
        return policy.Append(new RetryStrategy(options, policy.JudgeOrDefault));
    }

    /// <summary>Appends a retry that never gives up.</summary>
    public static Policy RetryForever(this Policy policy, Backoff? backoff = null) => policy.Retry(options =>
    {
        options.MaxRetries = int.MaxValue;
        options.Backoff = backoff ?? Backoff.Default;
    });

    /// <summary>Appends a timeout, surfacing <see cref="TimeoutExceededException"/> when exceeded.</summary>
    public static Policy Timeout(this Policy policy, TimeSpan timeout) => policy.Timeout(options => options.Timeout = timeout);

    /// <summary>Appends a timeout strategy configured via <paramref name="configure"/>.</summary>
    public static Policy Timeout(this Policy policy, Action<TimeoutOptions> configure)
    {
        Throw.IfNull(policy, nameof(policy));
        Throw.IfNull(configure, nameof(configure));
        var options = new TimeoutOptions();
        configure(options);
        return policy.Append(new TimeoutStrategy(options));
    }

    /// <summary>Appends a circuit breaker that opens for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive handled exceptions.</summary>
    public static Policy CircuitBreaker(this Policy policy, int consecutiveFailures, TimeSpan breakDuration) => policy.CircuitBreaker(options =>
    {
        options.ConsecutiveFailures = consecutiveFailures;
        options.BreakDuration = breakDuration;
    });

    /// <summary>Appends a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public static Policy CircuitBreaker(this Policy policy, Action<CircuitBreakerOptions> configure)
    {
        Throw.IfNull(policy, nameof(policy));
        Throw.IfNull(configure, nameof(configure));
        var options = new CircuitBreakerOptions();
        configure(options);
        return policy.Append(new CircuitBreakerStrategy(options, policy.JudgeOrDefault));
    }

    /// <summary>Appends a token-bucket rate limit of <paramref name="permits"/> executions per <paramref name="perWindow"/>.</summary>
    public static Policy RateLimit(this Policy policy, int permits, TimeSpan perWindow) => policy.RateLimit(options =>
    {
        options.Permits = permits;
        options.Window = perWindow;
    });

    /// <summary>Appends a rate limit strategy configured via <paramref name="configure"/>.</summary>
    public static Policy RateLimit(this Policy policy, Action<RateLimitOptions> configure)
    {
        Throw.IfNull(policy, nameof(policy));
        Throw.IfNull(configure, nameof(configure));
        var options = new RateLimitOptions();
        configure(options);
        return policy.Append(new RateLimitStrategy(options));
    }

    /// <summary>Appends a bulkhead capping concurrency at <paramref name="maxConcurrency"/> with an optional wait queue.</summary>
    public static Policy Bulkhead(this Policy policy, int maxConcurrency, int maxQueue = 0) => policy.Bulkhead(options =>
    {
        options.MaxConcurrency = maxConcurrency;
        options.MaxQueue = maxQueue;
    });

    /// <summary>Appends a bulkhead strategy configured via <paramref name="configure"/>.</summary>
    public static Policy Bulkhead(this Policy policy, Action<BulkheadOptions> configure)
    {
        Throw.IfNull(policy, nameof(policy));
        Throw.IfNull(configure, nameof(configure));
        var options = new BulkheadOptions();
        configure(options);
        return policy.Append(new BulkheadStrategy(options));
    }

    /// <summary>Appends hedging: up to <paramref name="maxAttempts"/> concurrent attempts staggered by <paramref name="delay"/>.</summary>
    public static Policy Hedge(this Policy policy, int maxAttempts, TimeSpan delay) => policy.Hedge(options =>
    {
        options.MaxAttempts = maxAttempts;
        options.Delay = delay;
    });

    /// <summary>Appends a hedging strategy configured via <paramref name="configure"/>.</summary>
    public static Policy Hedge(this Policy policy, Action<HedgingOptions> configure)
    {
        Throw.IfNull(policy, nameof(policy));
        Throw.IfNull(configure, nameof(configure));
        var options = new HedgingOptions();
        configure(options);
        return policy.Append(new HedgingStrategy(options, policy.JudgeOrDefault));
    }

    /// <summary>Starts a handling clause: subsequent reactive strategies act on exceptions of type <typeparamref name="TException"/>.</summary>
    public static PolicyBuilder Handle<TException>(this Policy policy)
        where TException : Exception
    {
        Throw.IfNull(policy, nameof(policy));
        return new PolicyBuilder(policy).Or<TException>();
    }

    /// <summary>Starts a handling clause for exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>.</summary>
    public static PolicyBuilder Handle<TException>(this Policy policy, Func<TException, bool> predicate)
        where TException : Exception
    {
        Throw.IfNull(policy, nameof(policy));
        return new PolicyBuilder(policy).Or(predicate);
    }

    /// <summary>Starts a handling clause for exceptions matching <paramref name="predicate"/>.</summary>
    public static PolicyBuilder HandleWhen(this Policy policy, Func<Exception, bool> predicate)
    {
        Throw.IfNull(policy, nameof(policy));
        return new PolicyBuilder(policy).OrWhen(predicate);
    }

    /// <summary>Appends a custom <see cref="Strategy"/> implementation to the pipeline.</summary>
    public static Policy Use(this Policy policy, Strategy strategy)
    {
        Throw.IfNull(policy, nameof(policy));
        Throw.IfNull(strategy, nameof(strategy));
        return policy.Append(strategy);
    }

    /// <summary>Wraps <paramref name="inner"/> inside <paramref name="outer"/>: the outer policy's strategies run first.</summary>
    public static Policy Wrap(this Policy outer, Policy inner)
    {
        Throw.IfNull(outer, nameof(outer));
        Throw.IfNull(inner, nameof(inner));
        return new Policy(Policy.Concat(outer.Strategies, inner.Strategies), outer.Ambient, outer.Name, outer.Time);
    }

    /// <summary>Wraps a result-aware <paramref name="inner"/> policy inside <paramref name="outer"/>, producing a result-aware policy.</summary>
    public static Policy<TResult> Wrap<TResult>(this Policy outer, Policy<TResult> inner)
    {
        Throw.IfNull(outer, nameof(outer));
        Throw.IfNull(inner, nameof(inner));
        return new Policy<TResult>(Policy.Concat(outer.Strategies, inner.Strategies), inner.Ambient, outer.Name ?? inner.Name, outer.Time ?? inner.Time);
    }

    /// <summary>Lifts this policy into a result-aware <see cref="Policy{TResult}"/> so result-handling clauses can be chained.</summary>
    public static Policy<TResult> For<TResult>(this Policy policy)
    {
        Throw.IfNull(policy, nameof(policy));
        return new Policy<TResult>(policy.Strategies, null, policy.Name, policy.Time);
    }

    /// <summary>Returns a copy of this policy with a diagnostic name (surfaced as <see cref="KevlarContext.PolicyName"/>).</summary>
    public static Policy WithName(this Policy policy, string name)
    {
        Throw.IfNull(policy, nameof(policy));
        Throw.IfNull(name, nameof(name));
        return new Policy(policy.Strategies, policy.Ambient, name, policy.Time);
    }

    /// <summary>Returns a copy of this policy using the given <see cref="TimeProvider"/> for delays, timeouts and time windows.</summary>
    public static Policy WithTimeProvider(this Policy policy, TimeProvider timeProvider)
    {
        Throw.IfNull(policy, nameof(policy));
        Throw.IfNull(timeProvider, nameof(timeProvider));
        return new Policy(policy.Strategies, policy.Ambient, policy.Name, timeProvider);
    }
}
