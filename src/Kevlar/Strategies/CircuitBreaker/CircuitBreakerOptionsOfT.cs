namespace Kevlar;

/// <summary>
/// Result-typed configuration for a circuit breaker strategy on a
/// <see cref="Shield{TResult}"/>.
/// </summary>
public sealed class CircuitBreakerOptions<TResult> : CircuitBreakerOptions
{
    /// <summary>
    /// Setting this — or <see cref="CircuitBreakerOptions.HandlesException"/> — makes this circuit
    /// breaker ignore the ambient <c>When…</c> handling clause; this predicate then selects the
    /// results it handles.
    /// </summary>
    /// <remarks>
    /// The ambient clause is started with <c>When…</c>/<c>WhenResult…</c> on a shield and continued
    /// with <c>Or…</c> on the builder it returns, and applies to every reactive strategy chained
    /// after it. These properties replace that clause for this strategy alone; they do not narrow it.
    /// </remarks>
    /// <seealso cref="HandlingClause"/>
    public Func<TResult, bool>? HandlesResult { get; set; }

    internal override bool HasHandlingOverride =>
        HandlesException is not null || HandlesResult is not null;
}
