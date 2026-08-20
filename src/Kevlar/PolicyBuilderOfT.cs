using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// Accumulates exception and result handling clauses for a <see cref="Policy{TResult}"/> chain.
/// Obtained via <c>Policy.For&lt;T&gt;()</c> or the <c>Handle</c>/<c>HandleResult</c> methods on
/// <see cref="Policy{TResult}"/>; finished by adding a strategy. The clauses become the policy's
/// ambient handling for the strategy added here and reactive strategies chained afterwards.
/// </summary>
public sealed class PolicyBuilder<TResult>
{
    private readonly Policy<TResult> _parent;
    private readonly List<Func<Exception, bool>> _exceptionPredicates = [];
    private readonly List<Func<TResult, bool>> _resultPredicates = [];

    internal PolicyBuilder(Policy<TResult> parent) => _parent = parent;

    /// <summary>Handle exceptions of type <typeparamref name="TException"/>.</summary>
    public PolicyBuilder<TResult> Handle<TException>()
        where TException : Exception
    {
        _exceptionPredicates.Add(static exception => exception is TException);
        return this;
    }

    /// <summary>Handle exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>.</summary>
    public PolicyBuilder<TResult> Handle<TException>(Func<TException, bool> predicate)
        where TException : Exception
    {
        Throw.IfNull(predicate, nameof(predicate));
        _exceptionPredicates.Add(exception => exception is TException typed && predicate(typed));
        return this;
    }

    /// <summary>Handle exceptions matching <paramref name="predicate"/>.</summary>
    public PolicyBuilder<TResult> HandleWhen(Func<Exception, bool> predicate)
    {
        Throw.IfNull(predicate, nameof(predicate));
        _exceptionPredicates.Add(predicate);
        return this;
    }

    /// <summary>Handle results matching <paramref name="predicate"/>.</summary>
    public PolicyBuilder<TResult> HandleResult(Func<TResult, bool> predicate)
    {
        Throw.IfNull(predicate, nameof(predicate));
        _resultPredicates.Add(predicate);
        return this;
    }

    /// <summary>Handle results equal to <paramref name="result"/>.</summary>
    public PolicyBuilder<TResult> HandleResult(TResult result)
    {
        _resultPredicates.Add(candidate => EqualityComparer<TResult>.Default.Equals(candidate, result));
        return this;
    }

    /// <summary>Retries handled outcomes up to <paramref name="maxRetries"/> times with the default exponential jittered backoff.</summary>
    public Policy<TResult> Retry(int maxRetries = 3) => Seal().Retry(maxRetries);

    /// <summary>Retries handled outcomes up to <paramref name="maxRetries"/> times with the given backoff.</summary>
    public Policy<TResult> Retry(int maxRetries, Backoff backoff) => Seal().Retry(maxRetries, backoff);

    /// <summary>Adds a retry strategy configured via <paramref name="configure"/>.</summary>
    public Policy<TResult> Retry(Action<RetryOptions> configure) => Seal().Retry(configure);

    /// <summary>Retries handled outcomes indefinitely.</summary>
    public Policy<TResult> RetryForever(Backoff? backoff = null) => Seal().RetryForever(backoff);

    /// <summary>Breaks the circuit for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive handled outcomes.</summary>
    public Policy<TResult> CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration) => Seal().CircuitBreaker(consecutiveFailures, breakDuration);

    /// <summary>Adds a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public Policy<TResult> CircuitBreaker(Action<CircuitBreakerOptions> configure) => Seal().CircuitBreaker(configure);

    /// <summary>Races concurrent attempts; a handled outcome launches the next attempt immediately.</summary>
    public Policy<TResult> Hedge(int maxAttempts, TimeSpan delay) => Seal().Hedge(maxAttempts, delay);

    /// <summary>Adds a hedging strategy configured via <paramref name="configure"/>.</summary>
    public Policy<TResult> Hedge(Action<HedgingOptions> configure) => Seal().Hedge(configure);

    /// <summary>Replaces handled outcomes with <paramref name="fallbackValue"/>.</summary>
    public Policy<TResult> Fallback(TResult fallbackValue, Action<FallbackEvent>? onFallback = null) => Seal().Fallback(fallbackValue, onFallback);

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/>.</summary>
    public Policy<TResult> Fallback(Func<CancellationToken, ValueTask<TResult>> fallback, Action<FallbackEvent>? onFallback = null) => Seal().Fallback(fallback, onFallback);

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/>, which receives the handled outcome.</summary>
    public Policy<TResult> Fallback(Func<Outcome<TResult>, CancellationToken, ValueTask<TResult>> fallback, Action<FallbackEvent>? onFallback = null) => Seal().Fallback(fallback, onFallback);

    /// <summary>Cancels executions that exceed <paramref name="timeout"/>. The handling clauses remain ambient for later strategies.</summary>
    public Policy<TResult> Timeout(TimeSpan timeout) => Seal().Timeout(timeout);

    /// <summary>Limits throughput. The handling clauses remain ambient for later strategies.</summary>
    public Policy<TResult> RateLimit(int permits, TimeSpan perWindow) => Seal().RateLimit(permits, perWindow);

    /// <summary>Caps concurrency. The handling clauses remain ambient for later strategies.</summary>
    public Policy<TResult> Bulkhead(int maxConcurrency, int maxQueue = 0) => Seal().Bulkhead(maxConcurrency, maxQueue);

    private Policy<TResult> Seal()
    {
        if (_exceptionPredicates.Count == 0 && _resultPredicates.Count == 0)
        {
            return _parent;
        }

        var exceptionPredicate = _exceptionPredicates.Count == 0 ? null : PolicyBuilder.Combine(_exceptionPredicates);
        var resultPredicate = CombineResults(_resultPredicates);
        var judge = new TypedJudge<TResult>(exceptionPredicate, resultPredicate);
        return new Policy<TResult>(_parent.Strategies, judge, _parent.Name, _parent.Time);
    }

    private static Func<TResult, bool>? CombineResults(List<Func<TResult, bool>> predicates)
    {
        if (predicates.Count == 0)
        {
            return null;
        }

        if (predicates.Count == 1)
        {
            return predicates[0];
        }

        var snapshot = predicates.ToArray();
        return result =>
        {
            foreach (var predicate in snapshot)
            {
                if (predicate(result))
                {
                    return true;
                }
            }

            return false;
        };
    }
}
