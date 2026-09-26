# Kevlar.Extensions.Diagnostics.HealthChecks

Health checks for registered Kevlar shields, retained partitions, and explicitly supplied circuit breaker monitors.

```csharp
using System;
using Kevlar;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var services = new ServiceCollection();
services.AddLogging();
services.AddShield("catalog", Shield.CircuitBreaker(
    consecutiveFailures: 3, breakDuration: TimeSpan.FromSeconds(30)));
services.AddHealthChecks().AddKevlar(name: "kevlar", failureStatus: HealthStatus.Degraded);
```

Open and half-open circuits are degraded by default; isolated circuits are unhealthy. Use
`configure` to change `IsolatedStatus`, set a `ShieldFilter`, or add named `Monitors`.
Set `IncludeRegistry = false` for monitor-only checks or custom registry implementations.

Report data includes every inspected breaker state and retained-partition counts without partition keys.
Inspection builds selected lazy registry entries but never executes protected operations or creates partitions.
See the [health check guide](https://thomhurst.github.io/Kevlar/docs/health-checks) for selection, report data, and readiness guidance.
