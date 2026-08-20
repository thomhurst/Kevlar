---
sidebar_position: 11
---

# Observability

Shields are observable without any setup: they describe themselves as strings, publish metrics through a built-in `Meter`, and an analyzer package catches the most common resilience mistake at compile time.

## Pipeline descriptions

`shield.ToString()` prints the whole pipeline, outermost strategy first, with each strategy's configuration:

```csharp
var shield = Shield
    .Timeout(TimeSpan.FromSeconds(30))
    .Retry(3)
    .CircuitBreaker(5, TimeSpan.FromSeconds(30))
    .WithName("github");

Console.WriteLine(shield);
// github: Timeout(30s) → Retry(3, exponential 250ms ×2 +jitter ≤30s) → CircuitBreaker(5 consecutive, break 30s)
```

Log it once at startup and every incident review starts from the actual configuration, not the configuration someone remembers. Custom strategies participate by overriding `Strategy.Describe()`.

## Metrics

On .NET 8+ every shield publishes metrics through a `System.Diagnostics.Metrics.Meter` named `Kevlar` — zero configuration, and effectively free (a branch per event) until something listens. Subscribe with OpenTelemetry:

```csharp
services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(KevlarDiagnostics.MeterName));
```

| Instrument | Counts | Tags |
|---|---|---|
| `kevlar.executions` | completed executions | `shield.name`, `outcome` (`success`/`failure`) |
| `kevlar.retries` | retry attempts | `shield.name` |
| `kevlar.timeouts` | executions cancelled by a timeout strategy | `shield.name` |
| `kevlar.hedges` | extra hedged attempts launched | `shield.name` |
| `kevlar.fallbacks` | outcomes replaced by a fallback | `shield.name` |
| `kevlar.rejections` | fail-fast rejections | `shield.name`, `kind` (`circuit_open`/`rate_limit`/`concurrency_limit`) |
| `kevlar.circuit_breaker.transitions` | circuit state changes | `from`, `to` |

The `shield.name` tag appears only for shields named via `WithName` — name the shields you dashboard. On `netstandard2.0` targets the instruments are inert (the metrics API isn't in-box there), keeping the core dependency-free.

## Compile-time checks

The `Kevlar.Analyzers` package ships Roslyn analyzers for mistakes that are otherwise invisible until an incident:

```bash
dotnet add package Kevlar.Analyzers
```

| Rule | Severity | Catches |
|---|---|---|
| `KEV001` | Warning | An execution delegate that never uses the `CancellationToken` it is handed — the most common way to defeat a [timeout](strategies/timeout.md). |

```csharp
await shield.ExecuteAsync(ct => client.GetAsync(url));        // KEV001: token ignored
await shield.ExecuteAsync(ct => client.GetAsync(url, ct));    // clean
```

## Callbacks

Every reactive strategy also raises in-process events for logging — `OnRetry`, `OnTimeout`, `OnStateChanged`, `OnHedge`, `onFallback` — documented on each [strategy page](/docs/category/strategies). Metrics tell you *how much*; callbacks give you the *which request* detail.
