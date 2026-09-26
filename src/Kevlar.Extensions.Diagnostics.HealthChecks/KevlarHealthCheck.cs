using Kevlar.Extensions.DependencyInjection;
using Kevlar.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kevlar.Extensions.Diagnostics.HealthChecks;

internal sealed class KevlarHealthCheck(
    IServiceProvider serviceProvider,
    bool includeRegistry,
    Func<string, bool>? filter,
    HealthStatus isolatedStatus,
    KeyValuePair<string, CircuitBreakerMonitor>[] monitors) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = HealthStatus.Healthy;
        var shields = new List<object>();
        var partitions = new List<object>();
        var monitorStates = new Dictionary<string, object>(StringComparer.Ordinal);

        string[] Report(CircuitState[] states)
        {
            foreach (var state in states)
            {
                var candidate = state switch
                {
                    CircuitState.Isolated => isolatedStatus,
                    CircuitState.Open or CircuitState.HalfOpen => context.Registration.FailureStatus,
                    _ => HealthStatus.Healthy
                };
                if (candidate < status)
                {
                    status = candidate;
                }
            }

            return states.Select(static state => state.ToString()).ToArray();
        }

        if (includeRegistry)
        {
            var registry = serviceProvider.GetService<IKevlarRegistry>();
            if (registry is not null && registry is not KevlarRegistry)
            {
                throw new InvalidOperationException(
                    "Automatic health checks require the built-in Kevlar registry. Set IncludeRegistry to false and supply monitors for a custom registry.");
            }

            if (registry is KevlarRegistry registered)
            {
                foreach (var shield in registered.CaptureShields(filter, cancellationToken))
                {
                    shields.Add(new Dictionary<string, object>
                    {
                        ["name"] = shield.Name,
                        ["resultType"] = shield.ResultType?.AssemblyQualifiedName ?? "",
                        ["breakers"] = Report(CaptureStates(shield.Strategies))
                    });
                }
            }

            // DI uses the last registration for each keyed service identity.
            foreach (var group in serviceProvider.GetServices<PartitionedShieldRegistration>()
                .GroupBy(static registration => (registration.Name, registration.ServiceType)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var registration = group.Last();
                if (filter is not null && !filter(registration.Name))
                {
                    continue;
                }

                var states = registration.Capture(serviceProvider).Select(CaptureStates).ToArray();
                partitions.Add(new Dictionary<string, object>
                {
                    ["name"] = registration.Name,
                    ["serviceType"] = registration.ServiceType.AssemblyQualifiedName!,
                    ["partitionCount"] = states.Length,
                    ["openPartitionCount"] = states.Count(static partition => partition.Contains(CircuitState.Open)),
                    ["halfOpenPartitionCount"] = states.Count(static partition => partition.Contains(CircuitState.HalfOpen)),
                    ["isolatedPartitionCount"] = states.Count(static partition => partition.Contains(CircuitState.Isolated)),
                    ["breakers"] = states.Select(Report).ToArray()
                });
            }
        }

        foreach (var monitor in monitors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            monitorStates.Add(monitor.Key, Report(monitor.Value.CaptureStates()));
        }

        IReadOnlyDictionary<string, object> data = new Dictionary<string, object>
        {
            ["shields"] = shields.ToArray(),
            ["partitions"] = partitions.ToArray(),
            ["monitors"] = monitorStates
        };
        return Task.FromResult(new HealthCheckResult(status, "Kevlar circuit breaker states.", data: data));
    }

    private static CircuitState[] CaptureStates(IEnumerable<Strategy> strategies) =>
        strategies.OfType<CircuitBreakerStrategy>().Select(static strategy => strategy.Core.State).ToArray();
}
