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

On .NET 8+ every shield publishes metrics through a `System.Diagnostics.Metrics.Meter` named `Kevlar`, version `1.0` — zero configuration, and effectively free (an enabled check per instrument) until something listens. Subscribe with OpenTelemetry:

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
| `kevlar.execution.duration` | `s` | histogram of completed public execution duration | `kevlar.shield.name`, `kevlar.execution.outcome` (`success`/`failure`) |
| `kevlar.circuit_breaker.state` | `{state}` | last observed circuit state: closed `0`, open `1`, half-open `2`, isolated `3` | `kevlar.shield.name` |
| `kevlar.concurrency_limit.inflight` | `{execution}` | executions holding a permit | `kevlar.shield.name` |
| `kevlar.concurrency_limit.queued` | `{execution}` | executions waiting for a permit | `kevlar.shield.name` |
| `kevlar.concurrency_limit.capacity` | `{execution}` | configured concurrency permit capacity | `kevlar.shield.name` |
| `kevlar.rate_limit.available` | `{permit}` | immediately available burst permits at the last limiter operation | `kevlar.shield.name` |
| `kevlar.rate_limit.queued` | `{execution}` | executions waiting for a rate-limit permit | `kevlar.shield.name` |

Each public execution call records exactly one `kevlar.executions` measurement after its final outcome: recovery through fallback is `success`; exceptions, caller cancellation, timeout, and strategy rejection are `failure`. Retry and hedge attempts do not add execution measurements of their own.

The `kevlar.shield.name` attribute appears only for shields named via `WithName` — name the shields you plan to chart. `WithName("")` emits the attribute with an empty value; an unnamed shield omits it. Instrument and attribute names use the product-specific `kevlar` namespace; count units use singular UCUM annotations. Counters and the duration histogram require .NET 8 or later; the shipped state gauges require .NET 10 or later. On `netstandard2.0` targets the instruments are inert because the metrics API isn't in-box there.

The gauges are synchronous last-value measurements emitted when strategy state changes. They aggregate by shield name and carry a bounded `kevlar.strategy.index` attribute (the strategy's zero-based pipeline position), so independent stateful strategies in one named pipeline remain distinct. Shared strategies update up to 64 observed name/index aliases; additional aliases omit state-gauge measurements to bound memory, transition work, and series growth. The gauges do not use observable callbacks or global strategy registries, so telemetry never keeps an abandoned shield alive. A typical Prometheus export can query p95 latency and queue saturation with:

```promql
histogram_quantile(0.95, sum by (le) (rate(kevlar_execution_duration_seconds_bucket[5m])))
max by (kevlar_shield_name) (kevlar_concurrency_limit_queued)
```

Exporter naming rules vary; inspect the exported names if your backend applies a different dot/unit translation.

### Telemetry overhead

BenchmarkDotNet `ShortRun` results on .NET 10.0.11, Windows 11, and an Intel Core i7-12700K measured no managed allocation in any case. Listener-enabled timings include an empty `MeterListener` receiving every Kevlar instrument:

| Pipeline | Listener off | Listener on | Allocated |
|---|---:|---:|---:|
| Empty shield | 3 ns | 237 ns | 0 B |
| Retry | 96 ns | 322 ns | 0 B |
| Circuit breaker | 114 ns | 432 ns | 0 B |
| Rate limit | 84 ns | 577 ns | 0 B |
| Concurrency limit | 131 ns | 636 ns | 0 B |

These figures are a local comparison rather than a performance guarantee. Run `TelemetryBenchmarks` on the deployment hardware to measure exporter and listener costs in that environment.

## Compile-time checks

The `Kevlar.Analyzers` package ships Roslyn analyzers for mistakes that are otherwise invisible until an incident:

```bash
dotnet add package Kevlar.Analyzers
```

| Rule | Severity | Catches |
|---|---|---|
| `KEV001` | Warning | An execution delegate that never uses the `CancellationToken` it is handed — the most common way to defeat a [timeout](strategies/timeout.md). |
| `KEV002` | Warning | A statically known multi-attempt hedging pipeline passed to synchronous `Execute`. |
| `KEV003` | Warning | An inner fallback that makes retry, hedging, or circuit breaker unreachable under the same handling clause. |

```csharp
await shield.ExecuteAsync(ct => client.GetAsync(url));        // KEV001: token ignored
await shield.ExecuteAsync(ct => client.GetAsync(url, ct));    // clean
```

See [Analyzer rules](analyzers.md) for rationale, safe alternatives, conservative analysis limits,
and suppression guidance.

## Callbacks

Every reactive strategy also raises in-process events for logging — `OnRetry`, `OnTimeout`, `OnStateChanged`, `OnHedge`, `onFallback` — documented on each [strategy page](/docs/category/strategies). Metrics tell you *how much*; callbacks give you the *which request* detail.
