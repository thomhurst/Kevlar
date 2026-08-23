using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// Accumulates exception-handling clauses for a <see cref="Shield"/> chain. Obtained via
/// <c>Shield.When&lt;T&gt;()</c> or <c>shield.When&lt;T&gt;()</c>; finished by adding a
/// strategy. The clauses become the shield's ambient handling: they apply to the strategy added
/// here and to reactive strategies chained afterwards, until replaced by a new clause.
/// </summary>
/// <remarks>
/// Start a clause with <c>When…</c> on a shield, then continue it with <c>Or…</c> on this builder:
/// <c>Shield.When&lt;A&gt;().Or&lt;B&gt;().Retry(3)</c>.
/// </remarks>
public sealed class ShieldBuilder
{
    private readonly Shield _parent;
    private readonly List<Func<Exception, bool>> _predicates = [];

    internal ShieldBuilder(Shield parent) => _parent = parent;

    /// <summary>Also handle exceptions of type <typeparamref name="TException"/>.</summary>
    public ShieldBuilder Or<TException>()
        where TException : Exception
    {
        _predicates.Add(static exception => exception is TException);
        return this;
    }

    /// <summary>Also handle exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>.</summary>
    public ShieldBuilder Or<TException>(Func<TException, bool> predicate)
        where TException : Exception
    {
        Throw.IfNull(predicate, nameof(predicate));
        _predicates.Add(exception => exception is TException typed && predicate(typed));
        return this;
    }

    /// <summary>Also handle exceptions matching <paramref name="predicate"/>.</summary>
    public ShieldBuilder OrWhen(Func<Exception, bool> predicate)
    {
        Throw.IfNull(predicate, nameof(predicate));
        return Or(predicate);
    }

    private ShieldBuilder Or(Func<Exception, bool> predicate)
    {
        _predicates.Add(predicate);
        return this;
    }

    /// <summary>Retries handled exceptions up to <paramref name="maxRetries"/> times with the default exponential jittered backoff.</summary>
    public Shield Retry(int maxRetries = 3) => Seal().Retry(maxRetries);

    /// <summary>Retries handled exceptions up to <paramref name="maxRetries"/> times with the given backoff.</summary>
    public Shield Retry(int maxRetries, Backoff backoff) => Seal().Retry(maxRetries, backoff);

    /// <summary>Adds a retry strategy configured via <paramref name="configure"/>.</summary>
    public Shield Retry(Action<RetryOptions> configure) => Seal().Retry(configure);

    /// <summary>Retries handled exceptions indefinitely.</summary>
    public Shield RetryForever(Backoff? backoff = null) => Seal().RetryForever(backoff);

    /// <summary>Breaks the circuit for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive handled exceptions.</summary>
    public Shield CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration) => Seal().CircuitBreaker(consecutiveFailures, breakDuration);

    /// <summary>Adds a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public Shield CircuitBreaker(Action<CircuitBreakerOptions> configure) => Seal().CircuitBreaker(configure);

    /// <summary>Races concurrent attempts; a handled exception launches the next attempt immediately.</summary>
    public Shield Hedge(int maxAttempts, TimeSpan delay) => Seal().Hedge(maxAttempts, delay);

    /// <summary>Adds a hedging strategy configured via <paramref name="configure"/>.</summary>
    public Shield Hedge(Action<HedgingOptions> configure) => Seal().Hedge(configure);

    /// <summary>
    /// Runs <paramref name="fallback"/> in place of handled failures. Applies to void executions
    /// only; result-returning executions need <c>Shield.For&lt;T&gt;().Fallback(…)</c>.
    /// </summary>
    public Shield Fallback(Func<Exception, CancellationToken, ValueTask> fallback) => Seal().Fallback(fallback);

    /// <summary>
    /// Runs <paramref name="fallback"/> in place of handled failures and uses the configured
    /// notifications. Applies to void executions only.
    /// </summary>
    public Shield FallbackWithNotifications(
        Func<Exception, CancellationToken, ValueTask> fallback,
        FallbackOptions options) => Seal().FallbackWithNotifications(fallback, options);

    /// <summary>Cancels executions that exceed <paramref name="timeout"/>. The handling clauses remain ambient for later strategies.</summary>
    public Shield Timeout(TimeSpan timeout) => Seal().Timeout(timeout);

    /// <summary>Limits throughput. The handling clauses remain ambient for later strategies.</summary>
    public Shield RateLimit(int permits, TimeSpan perWindow) => Seal().RateLimit(permits, perWindow);

    /// <summary>Adds a configured rate limit. The handling clauses remain ambient for later strategies.</summary>
    public Shield RateLimit(Action<RateLimitOptions> configure) => Seal().RateLimit(configure);

    /// <summary>Caps concurrency. The handling clauses remain ambient for later strategies.</summary>
    public Shield ConcurrencyLimit(int maxConcurrency, int maxQueue = 0) => Seal().ConcurrencyLimit(maxConcurrency, maxQueue);

    /// <summary>Adds a configured concurrency limit. The handling clauses remain ambient for later strategies.</summary>
    public Shield ConcurrencyLimit(Action<ConcurrencyLimitOptions> configure) => Seal().ConcurrencyLimit(configure);

    private Shield Seal() =>
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
