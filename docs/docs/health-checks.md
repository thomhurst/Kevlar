---
sidebar_position: 15
---

# Health checks

`Kevlar.Extensions.Diagnostics.HealthChecks` reports circuit breaker state through .NET health checks.
Add the package to an application that uses `AddHealthChecks`, then register the check:

```csharp
using System;
using Kevlar;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var services = new ServiceCollection();
services.AddLogging(); // ASP.NET Core hosts already register logging.
services.AddShield("catalog", Shield.CircuitBreaker(
    consecutiveFailures: 3, breakDuration: TimeSpan.FromSeconds(30)));
services.AddHealthChecks()
    .AddKevlar(name: "kevlar", failureStatus: HealthStatus.Degraded);
```

The check resolves registered shields through `IKevlarRegistry`, including typed shields and
registrations added dynamically. Reloading shields report only their current publication.
Inspection reads state without executing an operation, admitting a half-open probe, or resetting a breaker.
A selected lazy registration is built on its first inspection; factories should finish promptly.

## Status and selection

Closed circuits are healthy. Open and half-open circuits use `failureStatus` (degraded by default).
Isolated circuits are unhealthy by default. The check returns the worst configured health status
across all selected circuits. Configure isolated status, filter by shield name, or supply monitors:

```csharp
using System;
using Kevlar;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var services = new ServiceCollection();
services.AddLogging();
var monitor = new CircuitBreakerMonitor();
var externalShield = Shield.CircuitBreaker(options => options.Monitor = monitor);

services.AddHealthChecks().AddKevlar(
    name: "dependencies",
    failureStatus: HealthStatus.Unhealthy,
    configure: options =>
    {
        options.IsolatedStatus = HealthStatus.Degraded;
        options.ShieldFilter = name => name.StartsWith("critical-", StringComparison.Ordinal);
        options.Monitors.Add("external", monitor);
    },
    tags: ["ready"]);
```

The filter runs before resolving registry factories and also filters named partition providers.
Explicit monitors are always included; a monitor can report multiple bound breakers.
Options and the monitor collection are copied at registration. The filter delegate and monitor
instances remain shared; callers must make their own mutable state safe for concurrent inspections.

Set `IncludeRegistry = false` for a monitor-only check. Automatic enumeration requires the built-in
registry; custom `IKevlarRegistry` implementations can supply explicit monitors with enumeration disabled.
An unbound monitor, failed factory, or unsupported registry produces the registration's failure status
and exposes the exception through the health-check service. An empty selection is healthy.

## Retained partitions and report data

Both typed and untyped providers registered with `AddPartitionedShield` are included automatically.
Inspection snapshots retained partitions; it never creates partitions, refreshes their idle expiration,
or exposes partition keys. Removal and eviction affect subsequent reports. Each report is an
observational snapshot, not a transaction across concurrent executions or registry changes.

`HealthReportEntry.Data` contains these values:

| Key | Value |
| --- | --- |
| `shields` | Array of records containing `name`, `resultType` (empty for untyped), and `breakers` (state strings in pipeline order). |
| `monitors` | Dictionary from configured monitor name to an array of bound circuit state strings. |
| `partitions` | Array of records containing `name`, `serviceType`, `partitionCount`, `openPartitionCount`, `halfOpenPartitionCount`, `isolatedPartitionCount`, and `breakers` (an array of state arrays). |

A partition with multiple open breakers counts once in `openPartitionCount`. Half-open and isolated
partitions have separate counts; a partition containing different breaker states may appear in more
than one count. Partition array positions are temporary and must not be treated as stable identities.
Type fields use assembly-qualified names to distinguish typed registrations.

Use the health-check service or a custom ASP.NET Core response writer to serialize report data;
the default endpoint response does not include this dictionary. Select this check for readiness
when circuit state should affect traffic routing. An open dependency circuit alone usually does
not require restarting the process through a liveness failure.
