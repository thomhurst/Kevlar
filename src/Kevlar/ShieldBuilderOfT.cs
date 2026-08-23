using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// Accumulates exception and result handling clauses for a <see cref="Shield{TResult}"/> chain.
/// Obtained via the <c>When</c>/<c>WhenResult</c> methods on <see cref="Shield{TResult}"/> and
/// finished by adding a strategy. The clauses become the shield's ambient handling for the
/// strategy added here and reactive strategies chained afterwards.
/// </summary>
/// <remarks>
/// Start a clause with <c>When…</c> on a shield, then continue it with <c>Or…</c> on this builder:
/// <c>Shield.For&lt;T&gt;().When&lt;A&gt;().OrResult(r =&gt; …).Retry(3)</c>.
/// </remarks>
public sealed class ShieldBuilder<TResult>
{
    private const string LegacyFallbackCallbackMessage =
        "Configure fallback notifications with Fallback(..., options => options.OnFallback = callback).";

    private readonly Shield<TResult> _parent;
    private readonly List<Func<Exception, bool>> _exceptionPredicates = [];
    private readonly List<Func<TResult, bool>> _resultPredicates = [];

    internal ShieldBuilder(Shield<TResult> parent) => _parent = parent;

    /// <summary>Also handle exceptions of type <typeparamref name="TException"/>.</summary>
    public ShieldBuilder<TResult> Or<TException>()
        where TException : Exception
    {
        _exceptionPredicates.Add(static exception => exception is TException);
        return this;
    }

    /// <summary>Also handle exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>.</summary>
    public ShieldBuilder<TResult> Or<TException>(Func<TException, bool> predicate)
        where TException : Exception
    {
        Throw.IfNull(predicate, nameof(predicate));
        _exceptionPredicates.Add(exception => exception is TException typed && predicate(typed));
        return this;
    }

    /// <summary>Also handle exceptions matching <paramref name="predicate"/>.</summary>
    public ShieldBuilder<TResult> OrWhen(Func<Exception, bool> predicate)
    {
        Throw.IfNull(predicate, nameof(predicate));
        _exceptionPredicates.Add(predicate);
        return this;
    }

    /// <summary>Also handle results matching <paramref name="predicate"/>.</summary>
    public ShieldBuilder<TResult> OrResult(Func<TResult, bool> predicate)
    {
        Throw.IfNull(predicate, nameof(predicate));
        _resultPredicates.Add(predicate);
        return this;
    }

    /// <summary>Also handle results equal to <paramref name="result"/>.</summary>
    public ShieldBuilder<TResult> OrResult(TResult result)
    {
        _resultPredicates.Add(candidate => EqualityComparer<TResult>.Default.Equals(candidate, result));
        return this;
    }

    /// <summary>Also handle results equal to <c>default</c> — <see langword="null"/> for reference types.</summary>
    public ShieldBuilder<TResult> OrDefault()
    {
        _resultPredicates.Add(static candidate => EqualityComparer<TResult>.Default.Equals(candidate, default!));
        return this;
    }

    /// <summary>Retries handled outcomes up to <paramref name="maxRetries"/> times with the default exponential jittered backoff.</summary>
    public Shield<TResult> Retry(int maxRetries = 3) => Seal().Retry(maxRetries);

    /// <summary>Retries handled outcomes up to <paramref name="maxRetries"/> times with the given backoff.</summary>
    public Shield<TResult> Retry(int maxRetries, Backoff backoff) => Seal().Retry(maxRetries, backoff);

    /// <summary>Adds a retry strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> Retry(Action<RetryOptions<TResult>> configure) => Seal().Retry(configure);

    /// <summary>Retries handled outcomes indefinitely.</summary>
    public Shield<TResult> RetryForever(Backoff? backoff = null) => Seal().RetryForever(backoff);

    /// <summary>Breaks the circuit for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive handled outcomes.</summary>
    public Shield<TResult> CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration) => Seal().CircuitBreaker(consecutiveFailures, breakDuration);

    /// <summary>Adds a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> CircuitBreaker(Action<CircuitBreakerOptions<TResult>> configure) => Seal().CircuitBreaker(configure);

    /// <summary>Races concurrent attempts; a handled outcome launches the next attempt immediately.</summary>
    public Shield<TResult> Hedge(int maxAttempts, TimeSpan delay) => Seal().Hedge(maxAttempts, delay);

    /// <summary>Adds a hedging strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> Hedge(Action<HedgingOptions<TResult>> configure) => Seal().Hedge(configure);

    /// <summary>Creates and appends a custom strategy using the accumulated handling clause.</summary>
    public Shield<TResult> Use(Func<HandlingClause, Strategy> factory) => Seal().Use(factory);

    /// <summary>Replaces handled outcomes with <paramref name="fallbackValue"/>.</summary>
    public Shield<TResult> Fallback(TResult fallbackValue) => Seal().Fallback(fallbackValue);

    /// <summary>Prevents legacy notification callbacks from binding as options configurators.</summary>
    [Obsolete(LegacyFallbackCallbackMessage, error: true)]
    public Shield<TResult> Fallback(TResult fallbackValue, Action<FallbackEvent<TResult>> onFallback) =>
        Seal().Fallback(fallbackValue, options => options.OnFallback = onFallback);

    /// <summary>Replaces handled outcomes with <paramref name="fallbackValue"/> and configures notifications.</summary>
    public Shield<TResult> Fallback(TResult fallbackValue, Action<FallbackOptions<TResult>> configure) =>
        Seal().Fallback(fallbackValue, configure);

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/>.</summary>
    public Shield<TResult> Fallback(Func<CancellationToken, ValueTask<TResult>> fallback) => Seal().Fallback(fallback);

    /// <summary>Prevents legacy notification callbacks from binding as options configurators.</summary>
    [Obsolete(LegacyFallbackCallbackMessage, error: true)]
    public Shield<TResult> Fallback(
        Func<CancellationToken, ValueTask<TResult>> fallback,
        Action<FallbackEvent<TResult>> onFallback) =>
        Seal().Fallback(fallback, options => options.OnFallback = onFallback);

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/> and configures notifications.</summary>
    public Shield<TResult> Fallback(
        Func<CancellationToken, ValueTask<TResult>> fallback,
        Action<FallbackOptions<TResult>> configure) => Seal().Fallback(fallback, configure);

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/>, which receives the handled outcome.</summary>
    public Shield<TResult> Fallback(Func<Outcome<TResult>, CancellationToken, ValueTask<TResult>> fallback) => Seal().Fallback(fallback);

    /// <summary>Prevents legacy notification callbacks from binding as options configurators.</summary>
    [Obsolete(LegacyFallbackCallbackMessage, error: true)]
    public Shield<TResult> Fallback(
        Func<Outcome<TResult>, CancellationToken, ValueTask<TResult>> fallback,
        Action<FallbackEvent<TResult>> onFallback) =>
        Seal().Fallback(fallback, options => options.OnFallback = onFallback);

    /// <summary>
    /// Replaces handled outcomes with the result of <paramref name="fallback"/>, which receives
    /// the handled outcome, and configures notifications.
    /// </summary>
    public Shield<TResult> Fallback(
        Func<Outcome<TResult>, CancellationToken, ValueTask<TResult>> fallback,
        Action<FallbackOptions<TResult>> configure) => Seal().Fallback(fallback, configure);

    /// <summary>Cancels executions that exceed <paramref name="timeout"/>. The handling clauses remain ambient for later strategies.</summary>
    public Shield<TResult> Timeout(TimeSpan timeout) => Seal().Timeout(timeout);

    /// <summary>Limits throughput. The handling clauses remain ambient for later strategies.</summary>
    public Shield<TResult> RateLimit(int permits, TimeSpan perWindow) => Seal().RateLimit(permits, perWindow);

    /// <summary>Adds a configured rate limit. The handling clauses remain ambient for later strategies.</summary>
    public Shield<TResult> RateLimit(Action<RateLimitOptions> configure) => Seal().RateLimit(configure);

    /// <summary>Caps concurrency. The handling clauses remain ambient for later strategies.</summary>
    public Shield<TResult> ConcurrencyLimit(int maxConcurrency, int maxQueue = 0) => Seal().ConcurrencyLimit(maxConcurrency, maxQueue);

    /// <summary>Adds a configured concurrency limit. The handling clauses remain ambient for later strategies.</summary>
    public Shield<TResult> ConcurrencyLimit(Action<ConcurrencyLimitOptions> configure) => Seal().ConcurrencyLimit(configure);

    private Shield<TResult> Seal()
    {
        var exceptionPredicate = _exceptionPredicates.Count == 0 ? null : ShieldBuilder.Combine(_exceptionPredicates);
        var resultPredicate = CombineResults(_resultPredicates);
        var judge = new TypedJudge<TResult>(exceptionPredicate, resultPredicate);
        return new Shield<TResult>(_parent.Strategies, judge, _parent.Name, _parent.Time);
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
