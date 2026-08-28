using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// An immutable exception and result handling clause under construction for a
/// <see cref="Shield{TResult}"/> chain. Obtained via the <c>When</c>/<c>WhenResult</c> methods on
/// <see cref="Shield{TResult}"/> and finished by adding a strategy. The clause becomes the shield's
/// ambient handling for the strategy added here and reactive strategies chained afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Start a clause with <c>When…</c> on a shield, then continue it with <c>Or…</c> on this builder:
/// <c>Shield.For&lt;T&gt;().When&lt;A&gt;().OrResult(r =&gt; …).Retry(3)</c>.
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
/// One strategy can opt out of the ambient clause: setting <c>HandlesException</c> or
/// <c>HandlesResult</c> on its options (<see cref="RetryOptions{TResult}"/>,
/// <see cref="CircuitBreakerOptions{TResult}"/>, <see cref="HedgeOptions{TResult}"/>,
/// <see cref="FallbackOptions{TResult}"/>) makes that strategy ignore the clause and handle only
/// what its own predicates select. Every other strategy in the chain keeps using the ambient clause.
/// </para>
/// </remarks>
public sealed class ShieldBuilder<TResult>
{
    private readonly Shield<TResult> _parent;
    private readonly Func<Exception, bool>[] _exceptionPredicates;
    private readonly Func<TResult, bool>[] _resultPredicates;
    private readonly Func<HandlingEvent<TResult>, bool>[] _contextPredicates;
    private readonly string[] _clauseTerms;

    internal ShieldBuilder(Shield<TResult> parent)
        : this(parent.CurrentSnapshot, [], [], [], [])
    {
    }

    private ShieldBuilder(
        Shield<TResult> parent,
        Func<Exception, bool>[] exceptionPredicates,
        Func<TResult, bool>[] resultPredicates,
        Func<HandlingEvent<TResult>, bool>[] contextPredicates,
        string[] clauseTerms)
    {
        _parent = parent;
        _exceptionPredicates = exceptionPredicates;
        _resultPredicates = resultPredicates;
        _contextPredicates = contextPredicates;
        _clauseTerms = clauseTerms;
    }

    /// <summary>Returns a new builder that also handles exceptions of type <typeparamref name="TException"/>.</summary>
    public ShieldBuilder<TResult> Or<TException>()
        where TException : Exception
        => WithException(static exception => exception is TException, typeof(TException).Name);

    /// <summary>Returns a new builder that also handles exceptions of type <typeparamref name="TException"/> matching <paramref name="predicate"/>.</summary>
    public ShieldBuilder<TResult> Or<TException>(Func<TException, bool> predicate)
        where TException : Exception
    {
        Throw.IfNull(predicate, nameof(predicate));
        return WithException(
            exception => exception is TException typed && predicate(typed),
            typeof(TException).Name + " matching predicate");
    }

    /// <summary>Returns a new builder that also handles exceptions matching <paramref name="predicate"/>, whatever their type.</summary>
    public ShieldBuilder<TResult> Or(Func<Exception, bool> predicate)
    {
        Throw.IfNull(predicate, nameof(predicate));
        return WithException(predicate, "exception predicate");
    }

    /// <summary>Returns a new builder that also handles outcomes selected using execution context.</summary>
    public ShieldBuilder<TResult> OrContext(Func<HandlingEvent<TResult>, bool> predicate)
    {
        Throw.IfNull(predicate, nameof(predicate));
        return WithContext(predicate, "custom");
    }

    /// <summary>Returns a new builder that also handles results matching <paramref name="predicate"/>.</summary>
    public ShieldBuilder<TResult> OrResult(Func<TResult, bool> predicate)
    {
        Throw.IfNull(predicate, nameof(predicate));
        return WithResult(predicate, "result predicate");
    }

    /// <summary>Returns a new builder that also handles results selected using execution context.</summary>
    public ShieldBuilder<TResult> OrResultContext(Func<HandlingEvent<TResult>, bool> predicate)
    {
        Throw.IfNull(predicate, nameof(predicate));
        return WithContext(
            handling => handling.Outcome.Exception is null && predicate(handling),
            "custom");
    }

    /// <summary>Returns a new builder that also handles results equal to <paramref name="result"/>.</summary>
    public ShieldBuilder<TResult> OrResultEquals(TResult result) =>
        WithResult(
            candidate => EqualityComparer<TResult>.Default.Equals(candidate, result),
            "result " + DescribeHelper.Value(result));

    /// <summary>Returns a new builder that also handles results equal to <c>default(TResult)</c> — <see langword="null"/> for reference types.</summary>
    /// <remarks>
    /// For a reference type prefer <see cref="ShieldResultExtensions.OrResultIsNull{TResult}(ShieldBuilder{TResult})"/>,
    /// which says what it matches. This overload stays for value types and generic code, where
    /// <c>default(TResult)</c> — <c>0</c>, <see langword="false"/> — may or may not be a failure.
    /// </remarks>
    public ShieldBuilder<TResult> OrResultIsDefault() => WithDefaultResult("default result");

    /// <summary>Retries handled outcomes up to <paramref name="maxRetries"/> times with the default exponential jittered backoff.</summary>
    /// <param name="maxRetries">
    /// The number of <em>retries</em>, not the number of attempts: <c>Retry(3)</c> makes up to 4
    /// total attempts — the initial call plus 3 retries.
    /// </param>
    public Shield<TResult> Retry(int maxRetries = 3) => Seal().Retry(maxRetries);

    /// <summary>Retries handled outcomes up to <paramref name="maxRetries"/> times with the given backoff.</summary>
    /// <param name="maxRetries">
    /// The number of <em>retries</em>, not the number of attempts: <c>Retry(3)</c> makes up to 4
    /// total attempts — the initial call plus 3 retries.
    /// </param>
    /// <param name="backoff">The delay computation applied between attempts.</param>
    public Shield<TResult> Retry(int maxRetries, Backoff backoff) => Seal().Retry(maxRetries, backoff);

    /// <summary>Adds a retry strategy configured via <paramref name="configure"/>.</summary>
    /// <remarks>
    /// <see cref="RetryOptions{TResult}.MaxRetries"/> counts <em>retries</em>, not attempts:
    /// <c>MaxRetries = 3</c> makes up to 4 total attempts — the initial call plus 3 retries.
    /// </remarks>
    public Shield<TResult> Retry(Action<RetryOptions<TResult>> configure) => Seal().Retry(configure);

    /// <summary>Retries handled outcomes indefinitely with the default exponential jittered backoff.</summary>
    public Shield<TResult> RetryForever() => Seal().RetryForever();

    /// <summary>Retries handled outcomes indefinitely with the given backoff.</summary>
    /// <param name="backoff">The delay computation applied between attempts.</param>
    public Shield<TResult> RetryForever(Backoff backoff) => Seal().RetryForever(backoff);

    /// <summary>Breaks the circuit for <paramref name="breakDuration"/> after <paramref name="consecutiveFailures"/> consecutive handled outcomes.</summary>
    public Shield<TResult> CircuitBreaker(int consecutiveFailures, TimeSpan breakDuration) => Seal().CircuitBreaker(consecutiveFailures, breakDuration);

    /// <summary>Adds a circuit breaker strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> CircuitBreaker(Action<CircuitBreakerOptions<TResult>> configure) => Seal().CircuitBreaker(configure);

    /// <summary>Races concurrent attempts; a handled outcome launches the next attempt immediately.</summary>
    public Shield<TResult> Hedge(int maxHedgedAttempts, TimeSpan delay) => Seal().Hedge(maxHedgedAttempts, delay);

    /// <summary>Adds a hedging strategy configured via <paramref name="configure"/>.</summary>
    public Shield<TResult> Hedge(Action<HedgeOptions<TResult>> configure) => Seal().Hedge(configure);

    /// <summary>Creates and appends a custom strategy using the accumulated handling clause.</summary>
    public Shield<TResult> Use(Func<HandlingClause, Strategy> factory) => Seal().Use(factory);

    /// <summary>
    /// Appends a custom strategy. The accumulated handling clause remains ambient for later strategies.
    /// </summary>
    public Shield<TResult> Use(Strategy strategy) => Seal().Use(strategy);

    /// <summary>Replaces handled outcomes with <paramref name="fallbackValue"/>.</summary>
    public Shield<TResult> FallbackTo(TResult fallbackValue) => Seal().FallbackTo(fallbackValue);

    /// <summary>Replaces handled outcomes with <paramref name="fallbackValue"/> and configures notifications.</summary>
    public Shield<TResult> FallbackTo(TResult fallbackValue, Action<FallbackOptions<TResult>> configure) =>
        Seal().FallbackTo(fallbackValue, configure);

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/>.</summary>
    public Shield<TResult> Fallback(Func<CancellationToken, ValueTask<TResult>> fallback) => Seal().Fallback(fallback);

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/> and configures notifications.</summary>
    public Shield<TResult> Fallback(
        Func<CancellationToken, ValueTask<TResult>> fallback,
        Action<FallbackOptions<TResult>> configure) => Seal().Fallback(fallback, configure);

    /// <summary>Replaces handled outcomes with the result of <paramref name="fallback"/>, which receives the handled outcome.</summary>
    public Shield<TResult> Fallback(Func<Outcome<TResult>, CancellationToken, ValueTask<TResult>> fallback) => Seal().Fallback(fallback);

    /// <summary>
    /// Replaces handled outcomes with the result of <paramref name="fallback"/>, which receives
    /// the handled outcome, and configures notifications.
    /// </summary>
    public Shield<TResult> Fallback(
        Func<Outcome<TResult>, CancellationToken, ValueTask<TResult>> fallback,
        Action<FallbackOptions<TResult>> configure) => Seal().Fallback(fallback, configure);

    /// <summary>Cancels executions that exceed <paramref name="timeout"/>. The handling clauses remain ambient for later strategies.</summary>
    public Shield<TResult> Timeout(TimeSpan timeout) => Seal().Timeout(timeout);

    /// <summary>
    /// Adds a configured timeout. The handling clause remains ambient for later strategies.
    /// </summary>
    public Shield<TResult> Timeout(Action<TimeoutOptions> configure) => Seal().Timeout(configure);

    /// <summary>Limits throughput. The handling clauses remain ambient for later strategies.</summary>
    public Shield<TResult> RateLimit(int permits, TimeSpan perWindow) => Seal().RateLimit(permits, perWindow);

    /// <summary>Adds a configured rate limit. The handling clauses remain ambient for later strategies.</summary>
    public Shield<TResult> RateLimit(Action<RateLimitOptions> configure) => Seal().RateLimit(configure);

    /// <summary>Caps concurrency. The handling clauses remain ambient for later strategies.</summary>
    public Shield<TResult> ConcurrencyLimit(int maxConcurrency, int queueLimit = 0) => Seal().ConcurrencyLimit(maxConcurrency, queueLimit);

    /// <summary>Adds a configured concurrency limit. The handling clauses remain ambient for later strategies.</summary>
    public Shield<TResult> ConcurrencyLimit(Action<ConcurrencyLimitOptions> configure) => Seal().ConcurrencyLimit(configure);

    /// <summary>
    /// Freezes this builder's clause into a shield. Both predicate arrays are already private and
    /// never mutated, and the description is rendered here, so no later chaining — on this builder
    /// or on any builder derived from it — can change the handling of a shield already built.
    /// </summary>
    private Shield<TResult> Seal()
    {
        var judge = new TypedJudge<TResult>(
            _exceptionPredicates,
            _resultPredicates,
            DescribeHelper.Clause(_clauseTerms),
            _contextPredicates);
        return new Shield<TResult>(
            _parent.Strategies,
            judge,
            _parent.Name,
            _parent.Time,
            _parent.AppliedDecorators);
    }

    /// <summary>
    /// Adds the <c>default(TResult)</c> term under the description its caller chose, so the one
    /// predicate reads as <c>default result</c> or <c>null result</c> depending on the spelling
    /// the clause was written with.
    /// </summary>
    internal ShieldBuilder<TResult> WithDefaultResult(string clauseTerm) =>
        WithResult(
            static candidate => EqualityComparer<TResult>.Default.Equals(candidate, default!),
            clauseTerm);

    /// <summary>Builds the successor holding this builder's terms plus one more exception term.</summary>
    private ShieldBuilder<TResult> WithException(Func<Exception, bool> predicate, string clauseTerm) =>
        new(
            _parent,
            ShieldBuilder.Append(_exceptionPredicates, predicate),
            _resultPredicates,
            _contextPredicates,
            ShieldBuilder.Append(_clauseTerms, clauseTerm));

    /// <summary>Builds the successor holding this builder's terms plus one more result term.</summary>
    private ShieldBuilder<TResult> WithResult(Func<TResult, bool> predicate, string clauseTerm) =>
        new(
            _parent,
            _exceptionPredicates,
            ShieldBuilder.Append(_resultPredicates, predicate),
            _contextPredicates,
            ShieldBuilder.Append(_clauseTerms, clauseTerm));

    private ShieldBuilder<TResult> WithContext(
        Func<HandlingEvent<TResult>, bool> predicate,
        string clauseTerm) =>
        new(
            _parent,
            _exceptionPredicates,
            _resultPredicates,
            ShieldBuilder.Append(_contextPredicates, predicate),
            ShieldBuilder.Append(_clauseTerms, clauseTerm));
}
