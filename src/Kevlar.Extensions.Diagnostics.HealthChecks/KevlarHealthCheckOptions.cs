using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kevlar.Extensions.Diagnostics.HealthChecks;

/// <summary>Configures circuit breaker inspection for a Kevlar health check.</summary>
public sealed class KevlarHealthCheckOptions
{
    /// <summary>Gets or sets the status reported for isolated circuits. Defaults to unhealthy.</summary>
    public HealthStatus IsolatedStatus { get; set; } = HealthStatus.Unhealthy;

    /// <summary>Gets or sets whether to inspect registered shields and partition providers. Defaults to true.</summary>
    /// <remarks>Disable this when supplying only explicit monitors or using a custom registry implementation.</remarks>
    public bool IncludeRegistry { get; set; } = true;

    /// <summary>Gets or sets an optional case-sensitive shield-name predicate applied before resolving registrations.</summary>
    /// <remarks>The predicate also filters partition providers; explicitly supplied monitors are always inspected.</remarks>
    public Func<string, bool>? ShieldFilter { get; set; }

    /// <summary>Gets the named monitors to inspect in addition to registered shields.</summary>
    /// <remarks>Every monitor must be bound to at least one circuit. The collection is copied during registration.</remarks>
    public IDictionary<string, CircuitBreakerMonitor> Monitors { get; } =
        new Dictionary<string, CircuitBreakerMonitor>(StringComparer.Ordinal);
}
