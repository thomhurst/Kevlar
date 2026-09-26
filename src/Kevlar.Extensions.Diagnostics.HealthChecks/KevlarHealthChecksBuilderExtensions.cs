using Kevlar.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers health checks for Kevlar circuit breakers.</summary>
public static class KevlarHealthChecksBuilderExtensions
{
    /// <summary>Inspects registered shields, retained partitions, and explicitly configured circuit breaker monitors.</summary>
    /// <param name="builder">The health check builder.</param>
    /// <param name="name">The health check registration name.</param>
    /// <param name="failureStatus">The status for open or half-open circuits, and inspection failures.</param>
    /// <param name="configure">Optional filters, monitors, and isolated-circuit status.</param>
    /// <param name="tags">Optional registration tags.</param>
    /// <param name="timeout">The health check timeout.</param>
    /// <returns>The supplied builder.</returns>
    public static IHealthChecksBuilder AddKevlar(
        this IHealthChecksBuilder builder,
        string name = "kevlar",
        HealthStatus failureStatus = HealthStatus.Degraded,
        Action<KevlarHealthCheckOptions>? configure = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        var options = new KevlarHealthCheckOptions();
        configure?.Invoke(options);
        if (!Enum.IsDefined(typeof(HealthStatus), failureStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(failureStatus));
        }

        if (!Enum.IsDefined(typeof(HealthStatus), options.IsolatedStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(configure), "The isolated status is invalid.");
        }

        var isolatedStatus = options.IsolatedStatus;
        var includeRegistry = options.IncludeRegistry;
        var filter = options.ShieldFilter;
        var monitors = options.Monitors.ToArray();
        if (monitors.Any(static entry => entry.Value is null))
        {
            throw new ArgumentException("Monitor registrations must not contain null values.", nameof(configure));
        }

        return builder.Add(new HealthCheckRegistration(
            name,
            serviceProvider => new KevlarHealthCheck(serviceProvider, includeRegistry, filter, isolatedStatus, monitors),
            failureStatus,
            tags,
            timeout));
    }
}
