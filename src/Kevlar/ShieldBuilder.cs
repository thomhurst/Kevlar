using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// An immutable exception-handling clause under construction for a <see cref="Shield"/> chain.
/// Obtained via <c>Shield.When&lt;T&gt;()</c> or <c>shield.When&lt;T&gt;()</c>; finished by adding a
/// strategy. The clause becomes the shield's ambient handling: it applies to the strategy added
/// here and to reactive strategies chained afterwards, until replaced by a new clause.
/// </summary>
/// <remarks>
/// <para>
/// Start a clause with <c>When…</c> on a shield, then continue it with <c>Or…</c> on this builder:
/// <c>Shield.When&lt;A&gt;().Or&lt;B&gt;().Retry(3)</c>.
/// </para>
/// <para>
/// The builder is immutable. Each <c>Or…</c> returns a <em>new</em> builder holding the terms
/// accumulated so far plus the one just added, and leaves the builder it was called on untouched.
/// A builder held in a variable can therefore be branched into two chains safely: each branch gets
/// only its own terms. The corollary is that code must use the builder each <c>Or…</c>
/// <em>returns</em> — calling <c>Or…</c> and discarding the result adds nothing to anything.
/// Adding a strategy freezes the clause of that builder, so a shield already built is never
/// changed by further chaining either.
/// </para>
/// <para>
/// One strategy can opt out of the ambient clause: setting <c>HandlesException</c> on its options
/// (<see cref="RetryOptions"/>, <see cref="CircuitBreakerOptions"/>, <see cref="HedgeOptions"/>,
/// <see cref="FallbackOptions"/>) makes that strategy ignore the clause and handle only what its
/// own predicate selects. Every other strategy in the chain keeps using the ambient clause.
/// </para>
/// </remarks>
public sealed class ShieldBuilder
{
    private readonly Shield _parent;
    private readonly Func<Exception, bool>[] _predicates;
    private readonly Func<HandlingEvent, bool>[] _contextPredicates;
    private readonly string[] _clauseTerms;

    internal ShieldBuilder(Shield parent)
        : this(parent, [], [], [])
    {
    }

    private ShieldBuilder(
        Shield parent,
        Func<Exception, bool>[] predicates,
        Func<HandlingEvent, bool>[] contextPredicates,
        string[] clauseTerms)
    {
        _parent = parent;
        _predicates = predicates;
        _contextPredicates = contextPredicates;
        _clauseTerms = clauseTerms;
    }

    /// <summary>Returns a new builder that also handles exceptions of type <typeparamref name="TException"/>.</summary>
    public ShieldBuilder Or<TException>()
        where TException : Exception
        => With(static exception => exception is TException, typeof(TException).Name);

    /// <summary>Returns a new builder that also handles exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>.</summary>
    public ShieldBuilder Or<TException>(Func<TException, bool> predicate)
        where TException : Exception
    {
        Throw.IfNull(predicate, nameof(predicate));
        return With(
            exception => exception is TException typed && predicate(typed),
            typeof(TException).Name + " matching predicate");
    }

    /// <summary>Returns a new builder that also handles exceptions matching <paramref name="predicate"/>, whatever their type.</summary>
    public ShieldBuilder Or(Func<Exception, bool> predicate)
    {
        Throw.IfNull(predicate, nameof(predicate));
        return With(predicate, "exception predicate");
    }

    /// <summary>Returns a new builder that also handles exceptions selected using execution context.</summary>
    public ShieldBuilder OrContext(Func<HandlingEvent, bool> predicate)
    {
        Throw.IfNull(predicate, nameof(predicate));
        return WithContext(predicate, "custom");
    }

    /// <summary>Retries handled exceptions up to <paramref name="maxRetries"/> times with the default exponential jittered backoff.</summary>
    /// <param name="maxRetries">
    /// The number of <em>retries</em>, not the number of attempts: <c>Retry(3)</c> makes up to 4
    /// total attempts — the initial call plus 3 retries.
    /// </param>
    public Shield Retry(int maxRetries = 3) => Seal().Retry(maxRetries);

    /// <summary>Retries handled exceptions up to <paramref name="maxRetries"/> times with the given backoff.</summary>
    /// <param name="maxRetries">
    /// The number of <em>retries</em>, not the number of attempts: <c>Retry(3)</c> makes up to 4
    /// total attempts — the initial call plus 3 retries.
    /// </param>
    /// <param name="backoff">The delay computation applied between attempts.</param>
    public Shield Retry(int maxRetries, Backoff backoff) => Seal().Retry(maxRetries, backoff);

    /// <summary>Adds a retry strategy configured via <paramref name="configure"/>.</summary>
    /// <remarks>
    /// <see cref="RetryOptions.MaxRetries"/> counts <em>retries</em>, not attempts:
    /// <c>MaxRetries = 3</c> makes up to 4 total attempts — the initial call plus 3 retries.
    /// </remarks>
    public Shield Retry(Action<RetryOptions> configure) => Seal().Retry(configure);

    /// <summary>Retries handled exceptions indefinitely with the default exponential jittered backoff.</summary>
    public Shield RetryForever() => Seal().RetryForever();

    /// <summary>Retries handled exceptions indefinitely with the given backoff.</summary>
    /// <param name="backoff">The delay computation applied between attempts.</param>
    public Shield RetryForever(Backoff backoff) => Seal().RetryForever(backoff);

    /// <summary>Breaks the circuit for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive handled exceptions.</summary>
    public Shield CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration) => Seal().CircuitBreaker(consecutiveFailures, breakDuration);

    /// <summary>Adds a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public Shield CircuitBreaker(Action<CircuitBreakerOptions> configure) => Seal().CircuitBreaker(configure);

    /// <summary>Races concurrent attempts; a handled exception launches the next attempt immediately.</summary>
    /// <remarks>
    /// Hedging on an untyped <see cref="Shield"/> runs the execution delegate more than once,
    /// concurrently, and only its exceptions can select a winner. The delegate must therefore be
    /// idempotent: duplicate writes, charges, or sends are otherwise observable side effects of a
    /// hedge that later loses. Prefer <c>Shield.For&lt;T&gt;()</c>, where result clauses decide which
    /// attempt is acceptable, or confirm the action is safe to repeat.
    /// </remarks>
    public Shield Hedge(int maxAttempts, TimeSpan delay) => Seal().Hedge(maxAttempts, delay);

    /// <summary>Adds a hedging strategy configured via <paramref name="configure"/>.</summary>
    /// <remarks>
    /// Hedging on an untyped <see cref="Shield"/> runs the execution delegate more than once,
    /// concurrently, so the delegate must be idempotent. Prefer <c>Shield.For&lt;T&gt;()</c>, or
    /// confirm the action is safe to repeat.
    /// </remarks>
    public Shield Hedge(Action<HedgeOptions> configure) => Seal().Hedge(configure);

    /// <summary>Creates and appends a custom strategy using the accumulated handling clause.</summary>
    public Shield Use(Func<HandlingClause, Strategy> factory) => Seal().Use(factory);

    /// <summary>
    /// Appends a custom strategy. The accumulated handling clause remains ambient for later strategies.
    /// </summary>
    public Shield Use(Strategy strategy) => Seal().Use(strategy);

    /// <summary>
    /// Runs <paramref name="fallback"/> in place of handled failures. Applies to void executions
    /// only; result-producing recovery needs a typed shield.
    /// </summary>
    public Shield Fallback(Func<CancellationToken, ValueTask> fallback) => Seal().Fallback(fallback);

    /// <summary>
    /// Runs <paramref name="fallback"/> in place of handled failures and configures notifications.
    /// Applies to void executions only.
    /// </summary>
    public Shield Fallback(
        Func<CancellationToken, ValueTask> fallback,
        Action<FallbackOptions> configure) => Seal().Fallback(fallback, configure);

    /// <summary>
    /// Runs <paramref name="fallback"/> in place of handled failures. Applies to void executions
    /// only; result-producing recovery needs <c>Shield.For&lt;T&gt;().FallbackTo(…)</c> for a constant
    /// value or a typed <c>Fallback(…)</c> factory.
    /// </summary>
    public Shield Fallback(Func<Exception, CancellationToken, ValueTask> fallback) => Seal().Fallback(fallback);

    /// <summary>
    /// Runs <paramref name="fallback"/> in place of handled failures and configures notifications.
    /// Applies to void executions only.
    /// </summary>
    public Shield Fallback(
        Func<Exception, CancellationToken, ValueTask> fallback,
        Action<FallbackOptions> configure) => Seal().Fallback(fallback, configure);

    /// <summary>Cancels executions that exceed <paramref name="timeout"/>. The handling clauses remain ambient for later strategies.</summary>
    public Shield Timeout(TimeSpan timeout) => Seal().Timeout(timeout);

    /// <summary>
    /// Adds a configured timeout. The handling clause remains ambient for later strategies.
    /// </summary>
    public Shield Timeout(Action<TimeoutOptions> configure) => Seal().Timeout(configure);

    /// <summary>Limits throughput. The handling clauses remain ambient for later strategies.</summary>
    public Shield RateLimit(int permits, TimeSpan perWindow) => Seal().RateLimit(permits, perWindow);

    /// <summary>Adds a configured rate limit. The handling clauses remain ambient for later strategies.</summary>
    public Shield RateLimit(Action<RateLimitOptions> configure) => Seal().RateLimit(configure);

    /// <summary>Caps concurrency. The handling clauses remain ambient for later strategies.</summary>
    public Shield ConcurrencyLimit(int maxConcurrency, int queueLimit = 0) => Seal().ConcurrencyLimit(maxConcurrency, queueLimit);

    /// <summary>Adds a configured concurrency limit. The handling clauses remain ambient for later strategies.</summary>
    public Shield ConcurrencyLimit(Action<ConcurrencyLimitOptions> configure) => Seal().ConcurrencyLimit(configure);

    /// <summary>
    /// Freezes this builder's clause into a shield. The predicate array is already private and
    /// never mutated, and the description is rendered here, so no later chaining — on this builder
    /// or on any builder derived from it — can change the handling of a shield already built.
    /// </summary>
    private Shield Seal()
    {
        var description = DescribeHelper.Clause(_clauseTerms);
        OutcomeJudge judge = _contextPredicates.Length == 0
            ? new ExceptionJudge(Combine(_predicates), description)
            : new ContextExceptionJudge(
                _predicates.Length == 0 ? null : Combine(_predicates),
                CombineContexts(_contextPredicates),
                description);
        return new Shield(_parent.Strategies, judge, _parent.Name, _parent.Time);
    }

    /// <summary>Builds the successor holding this builder's terms plus one more.</summary>
    private ShieldBuilder With(Func<Exception, bool> predicate, string clauseTerm) =>
        new(
            _parent,
            Append(_predicates, predicate),
            _contextPredicates,
            Append(_clauseTerms, clauseTerm));

    private ShieldBuilder WithContext(Func<HandlingEvent, bool> predicate, string clauseTerm) =>
        new(
            _parent,
            _predicates,
            Append(_contextPredicates, predicate),
            Append(_clauseTerms, clauseTerm));

    /// <summary>Copies <paramref name="source"/> with <paramref name="item"/> appended.</summary>
    internal static T[] Append<T>(T[] source, T item)
    {
        var appended = new T[source.Length + 1];
        Array.Copy(source, appended, source.Length);
        appended[source.Length] = item;
        return appended;
    }

    internal static Func<Exception, bool> Combine(Func<Exception, bool>[] predicates)
    {
        if (predicates.Length == 0)
        {
            throw new InvalidOperationException("No handling clauses were added.");
        }

        if (predicates.Length == 1)
        {
            return predicates[0];
        }

        return exception =>
        {
            foreach (var predicate in predicates)
            {
                if (predicate(exception))
                {
                    return true;
                }
            }

            return false;
        };
    }

    private static Func<HandlingEvent, bool> CombineContexts(Func<HandlingEvent, bool>[] predicates)
    {
        if (predicates.Length == 1)
        {
            return predicates[0];
        }

        return handling =>
            EvaluateContextPredicates(predicates, handling);
    }

    internal static bool EvaluateContextPredicates<TEvent>(
        Func<TEvent, bool>[] predicates,
        TEvent handling)
    {
        foreach (var predicate in predicates)
        {
            try
            {
                if (predicate(handling))
                {
                    return true;
                }
            }
            catch (Exception exception)
            {
                OutcomeJudge.ReportPredicateFailure(exception);
            }
        }

        return false;
    }
}
