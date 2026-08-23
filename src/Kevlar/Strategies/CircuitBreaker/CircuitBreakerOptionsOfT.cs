namespace Kevlar;

/// <summary>
/// Result-typed configuration for a circuit breaker strategy on a
/// <see cref="Shield{TResult}"/>.
/// </summary>
public sealed class CircuitBreakerOptions<TResult> : CircuitBreakerOptions
{
    /// <summary>
    /// Locally selects results handled by this circuit breaker. When either local predicate is
    /// set, this strategy ignores the ambient handling clause and handles only outcomes selected
    /// locally.
    /// </summary>
    public Func<TResult, bool>? HandlesResult { get; set; }

    internal override bool HasHandlingOverride =>
        HandlesException is not null || HandlesResult is not null;
}
