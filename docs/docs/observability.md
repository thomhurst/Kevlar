---
sidebar_position: 11
---

# Observability

Shields are observable without any setup: they describe themselves as strings, publish metrics through a built-in `Meter`, and an analyzer package catches the most common resilience mistake at compile time.

## Pipeline descriptions

`shield.ToString()` prints the whole pipeline, outermost strategy first, with each strategy's configuration:

<!-- doc-test-run: pipeline-description -->
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

On .NET 8+ every shield publishes metrics through a `System.Diagnostics.Metrics.Meter` named `Kevlar`, version `1.0` — zero configuration, and effectively free (a branch per event) until something listens. All instruments are `Counter<long>`. Subscribe with OpenTelemetry:

```csharp
services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(KevlarDiagnostics.MeterName));
```

| Instrument | Unit | Counts | Attributes |
|---|---|---|---|
| `kevlar.executions` | `{execution}` | completed public execution calls, including empty shields and pre-cancelled calls | `kevlar.shield.name`, `kevlar.execution.outcome` (`success`/`failure`) |
| `kevlar.retries` | `{retry}` | retry attempts | `kevlar.shield.name` |
| `kevlar.timeouts` | `{timeout}` | executions cancelled by a timeout strategy | `kevlar.shield.name` |
| `kevlar.hedges` | `{hedge}` | extra hedged attempts launched | `kevlar.shield.name` |
| `kevlar.fallbacks` | `{fallback}` | outcomes replaced by a fallback | `kevlar.shield.name` |
| `kevlar.rejections` | `{rejection}` | fail-fast rejections | `kevlar.shield.name`, `kevlar.rejection.type` (`circuit_open`/`rate_limit`/`concurrency_limit`) |
| `kevlar.circuit_breaker.transitions` | `{transition}` | circuit state changes | `kevlar.circuit_breaker.state.from`, `kevlar.circuit_breaker.state.to` (`closed`/`open`/`half_open`/`isolated`) |

Each public execution call records exactly one `kevlar.executions` measurement after its final outcome: recovery through fallback is `success`; exceptions, caller cancellation, timeout, and strategy rejection are `failure`. Retry and hedge attempts do not add execution measurements of their own.

The `kevlar.shield.name` attribute appears only for shields named via `WithName` — name the shields you dashboard. `WithName("")` emits the attribute with an empty value; an unnamed shield omits it. Instrument and attribute names use the product-specific `kevlar` namespace; count units use singular UCUM annotations. On `netstandard2.0` targets the instruments are inert because the metrics API isn't in-box there.

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
