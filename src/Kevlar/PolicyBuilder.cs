using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// Accumulates exception-handling clauses for a <see cref="Policy"/> chain. Obtained via
/// <c>Policy.Handle&lt;T&gt;()</c> or <c>policy.Handle&lt;T&gt;()</c>; finished by adding a
/// strategy. The clauses become the policy's ambient handling: they apply to the strategy added
/// here and to reactive strategies chained afterwards, until replaced by a new clause.
/// </summary>
public sealed class PolicyBuilder
{
    private readonly Policy _parent;
    private readonly List<Func<Exception, bool>> _predicates = [];

    internal PolicyBuilder(Policy parent) => _parent = parent;

    /// <summary>Also handle exceptions of type <typeparamref name="TException"/>.</summary>
    public PolicyBuilder Or<TException>()
        where TException : Exception
    {
        _predicates.Add(static exception => exception is TException);
        return this;
    }

    /// <summary>Also handle exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>.</summary>
    public PolicyBuilder Or<TException>(Func<TException, bool> predicate)
        where TException : Exception
    {
        Throw.IfNull(predicate, nameof(predicate));
        _predicates.Add(exception => exception is TException typed && predicate(typed));
        return this;
    }

    /// <summary>Also handle exceptions matching <paramref name="predicate"/>.</summary>
    public PolicyBuilder OrWhen(Func<Exception, bool> predicate)
    {
        Throw.IfNull(predicate, nameof(predicate));
        _predicates.Add(predicate);
        return this;
    }

    /// <summary>Retries handled exceptions up to <paramref name="maxRetries"/> times with the default exponential jittered backoff.</summary>
    public Policy Retry(int maxRetries = 3) => Seal().Retry(maxRetries);

    /// <summary>Retries handled exceptions up to <paramref name="maxRetries"/> times with the given backoff.</summary>
    public Policy Retry(int maxRetries, Backoff backoff) => Seal().Retry(maxRetries, backoff);

    /// <summary>Adds a retry strategy configured via <paramref name="configure"/>.</summary>
    public Policy Retry(Action<RetryOptions> configure) => Seal().Retry(configure);

    /// <summary>Retries handled exceptions indefinitely.</summary>
    public Policy RetryForever(Backoff? backoff = null) => Seal().RetryForever(backoff);

    /// <summary>Breaks the circuit for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive handled exceptions.</summary>
    public Policy CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration) => Seal().CircuitBreaker(consecutiveFailures, breakDuration);

    /// <summary>Adds a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public Policy CircuitBreaker(Action<CircuitBreakerOptions> configure) => Seal().CircuitBreaker(configure);

    /// <summary>Races concurrent attempts; a handled exception launches the next attempt immediately.</summary>
    public Policy Hedge(int maxAttempts, TimeSpan delay) => Seal().Hedge(maxAttempts, delay);

    /// <summary>Adds a hedging strategy configured via <paramref name="configure"/>.</summary>
    public Policy Hedge(Action<HedgingOptions> configure) => Seal().Hedge(configure);

    /// <summary>Cancels executions that exceed <paramref name="timeout"/>. The handling clauses remain ambient for later strategies.</summary>
    public Policy Timeout(TimeSpan timeout) => Seal().Timeout(timeout);

    /// <summary>Limits throughput. The handling clauses remain ambient for later strategies.</summary>
    public Policy RateLimit(int permits, TimeSpan perWindow) => Seal().RateLimit(permits, perWindow);

    /// <summary>Caps concurrency. The handling clauses remain ambient for later strategies.</summary>
    public Policy Bulkhead(int maxConcurrency, int maxQueue = 0) => Seal().Bulkhead(maxConcurrency, maxQueue);

    private Policy Seal() =>
        new(_parent.Strategies, new ExceptionJudge(Combine(_predicates)), _parent.Name, _parent.Time);

    internal static Func<Exception, bool> Combine(List<Func<Exception, bool>> predicates)
    {
        if (predicates.Count == 0)
        {
            throw new InvalidOperationException("No handling clauses were added.");
        }

        if (predicates.Count == 1)
        {
            return predicates[0];
        }

        var snapshot = predicates.ToArray();
        return exception =>
        {
            foreach (var predicate in snapshot)
            {
                if (predicate(exception))
                {
                    return true;
                }
            }

            return false;
        };
    }
}
