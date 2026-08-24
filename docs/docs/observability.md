---
sidebar_position: 14
---

# Observability

Shields describe their configured pipeline, publish metrics through built-in `Meter` instances, and expose strategy callbacks for request-level telemetry. The optional analyzer package catches resilience mistakes at compile time.

## Pipeline descriptions

`shield.ToString()` prints the whole pipeline, outermost strategy first, with each strategy's configuration:

<!-- doc-test-run: pipeline-description -->
```csharp
var shield = Shield
    .Timeout(TimeSpan.FromSeconds(30))
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30))
    .ConcurrencyLimit(10, queueLimit: 5)
    .WithName("github");

Console.WriteLine(shield);
// github: Timeout(30s) → Retry(3, exponential 250ms ×2 +jitter ≤30s) → CircuitBreaker(5 consecutive, break 30s) → ConcurrencyLimit(10, queue 5)
```

Log it once at startup and every incident review starts from the actual configuration, not the configuration someone remembers. Custom strategies participate by overriding `Strategy.Describe()`.

[Handling clauses](handling-failures.md) show up too, so "why didn't the breaker trip?" is answerable from the description alone. A `[when …]` prefix opens each run of strategies sharing a non-default clause, and a strategy whose options replaced that clause with `HandlesException`/`HandlesResult` is marked `(local handling)`:

```csharp
var shield = Shield
    .When<HttpRequestException>()
    .Or<TimeoutExceededException>()
    .Retry(3, Backoff.None)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));

Console.WriteLine(shield);
// [when HttpRequestException | TimeoutExceededException] Retry(3, no delay) → CircuitBreaker(5 consecutive, break 30s)
```

Shields that use only the default handling—ordinary exceptions, excluding cancellation, Kevlar's
fail-fast rejections, and fatal runtime failures—print exactly as before, with no prefix.

## Metrics

Kevlar publishes core metrics through a `System.Diagnostics.Metrics.Meter` named `Kevlar`, version `1.0`. The core and runtime extension packages contain instrumented `net8.0` and `net10.0` assets; only `netstandard2.0` consumers receive inert metric implementations. `Kevlar.Chaos` separately publishes its injection counter from its `net8.0` and `net10.0` assets through a meter named `Kevlar.Chaos`, version `1.0`.

Register the core meter with OpenTelemetry:

```csharp
services.AddOpenTelemetry().WithMetrics(metrics => metrics
    .AddMeter(KevlarDiagnostics.MeterName));
```

Applications that reference the optional `Kevlar.Chaos` package can register its meter separately:

```csharp
services.AddOpenTelemetry().WithMetrics(metrics => metrics
    .AddMeter(ChaosDiagnostics.MeterName));
```

| Instrument | Type | Unit | Minimum target | Measures | Attributes |
|---|---|---|---|---|---|
| `kevlar.executions` | Counter | `{execution}` | `net8.0` | completed public execution calls, including empty shields and pre-cancelled calls | `kevlar.shield.name`, `kevlar.execution.outcome` (`success`/`failure`) |
| `kevlar.retries` | Counter | `{retry}` | `net8.0` | retry attempts | `kevlar.shield.name` |
| `kevlar.timeouts` | Counter | `{timeout}` | `net8.0` | executions cancelled by a timeout strategy | `kevlar.shield.name` |
| `kevlar.hedges` | Counter | `{hedge}` | `net8.0` | extra hedged attempts launched | `kevlar.shield.name` |
| `kevlar.fallbacks` | Counter | `{fallback}` | `net8.0` | outcomes replaced by a fallback | `kevlar.shield.name` |
| `kevlar.rejections` | Counter | `{rejection}` | `net8.0` | fail-fast rejections | `kevlar.shield.name`, `kevlar.rejection.type` (`circuit_open`/`rate_limit`/`concurrency_limit`) |
| `kevlar.circuit_breaker.transitions` | Counter | `{transition}` | `net8.0` | circuit state changes | `kevlar.circuit_breaker.state.from`, `kevlar.circuit_breaker.state.to` (`closed`/`open`/`half_open`/`isolated`) |
| `kevlar.execution.duration` | Histogram | `s` | `net8.0` | completed public execution duration | `kevlar.shield.name`, `kevlar.execution.outcome` (`success`/`failure`) |
| `kevlar.circuit_breaker.state` | Gauge | `{state}` | `net10.0` | last observed circuit state: closed `0`, open `1`, half-open `2`, isolated `3` | `kevlar.shield.name`, `kevlar.strategy.index` |
| `kevlar.concurrency_limit.inflight` | Gauge | `{execution}` | `net10.0` | executions holding a permit | `kevlar.shield.name`, `kevlar.strategy.index` |
| `kevlar.concurrency_limit.queued` | Gauge | `{execution}` | `net10.0` | executions waiting for a permit | `kevlar.shield.name`, `kevlar.strategy.index` |
| `kevlar.concurrency_limit.capacity` | Gauge | `{execution}` | `net10.0` | configured concurrency permit capacity | `kevlar.shield.name`, `kevlar.strategy.index` |
| `kevlar.rate_limit.available` | Gauge | `{permit}` | `net10.0` | immediately available burst permits at the last limiter operation | `kevlar.shield.name`, `kevlar.strategy.index` |
| `kevlar.rate_limit.queued` | Gauge | `{execution}` | `net10.0` | executions waiting for a rate-limit permit | `kevlar.shield.name`, `kevlar.strategy.index` |
| `kevlar.chaos.injections` | Counter | `{injection}` | `net8.0` | chaos injections applied | `kevlar.chaos.kind`, `kevlar.shield.name`, `kevlar.chaos.operation`, `kevlar.chaos.environment` |

Each public execution call records exactly one `kevlar.executions` measurement after its final outcome: recovery through fallback is `success`; exceptions, caller cancellation, timeout, and strategy rejection are `failure`. Retry and hedge attempts do not add execution measurements of their own.

The `kevlar.shield.name` attribute appears only for shields named via `WithName` — name the shields you plan to chart. `WithName("")` emits the attribute with an empty value; an unnamed shield omits it. Optional chaos scope attributes are also omitted when unset. Instrument and attribute names use the product-specific `kevlar` namespace; count units use singular UCUM annotations. Counters and the duration histogram are active in the native `net8.0` and `net10.0` assets; the shipped state gauges require `net10.0`. On `netstandard2.0` targets the instruments are inert because the metrics API is not available in-box.

The gauges are synchronous last-value measurements emitted when strategy state changes. They aggregate by shield name and carry a bounded `kevlar.strategy.index` attribute (the strategy's zero-based pipeline position), so independent stateful strategies in one named pipeline remain distinct. Shared strategies update up to 64 observed name/index aliases; additional aliases omit state-gauge measurements to bound memory, transition work, and series growth. The gauges do not use observable callbacks or global strategy registries, so telemetry never keeps an abandoned shield alive.

This executable example verifies a completed execution with `MeterListener`:

<!-- doc-test-run: metrics-listener -->
```csharp
using System.Diagnostics.Metrics;

var executions = 0L;
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, activeListener) =>
{
    if (instrument.Meter.Name == KevlarDiagnostics.MeterName
        && instrument.Name == "kevlar.executions")
    {
        activeListener.EnableMeasurementEvents(instrument);
    }
};
listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
{
    if (instrument.Name == "kevlar.executions")
    {
        executions += value;
    }
});
listener.Start();

await Shield.Empty.ExecuteAsync(_ => ValueTask.CompletedTask);

if (executions != 1)
{
    throw new InvalidOperationException($"Expected one execution measurement; observed {executions}.");
}
```

A typical Prometheus export can query p95 latency and queue saturation with:

```promql
histogram_quantile(0.95, sum by (le) (rate(kevlar_execution_duration_seconds_bucket[5m])))
max by (kevlar_shield_name) (kevlar_concurrency_limit_queued)
```

Exporter naming rules vary; inspect the exported names if your backend applies a different dot/unit translation.

### Telemetry overhead

Telemetry cost depends on the runtime, pipeline, listener, exporter, and deployment hardware. See [Benchmarks](benchmarks.md) for the current methodology and generated results. Run `TelemetryBenchmarks` on deployment-class hardware to measure the cost in your environment.

## Compile-time checks

Install `Kevlar.Analyzers` to catch resilience mistakes during compilation:

```bash
dotnet add package Kevlar.Analyzers
```

See [Analyzer rules](analyzers.md) for the complete current rule set, rationale, safe alternatives, conservative analysis limits, and suppression guidance.

## Callbacks

Strategy callbacks expose request-level events. Every async callback is awaited before execution continues.

| Strategy | Synchronous | Asynchronous |
|---|---|---|
| Retry | `OnRetry` | `OnRetryAsync` |
| Circuit breaker | `OnStateChanged` | `OnStateChangedAsync` |
| Timeout | `OnTimeout` | `OnTimeoutAsync` |
| Hedging | `OnHedge` | `OnHedgeAsync` |
| Fallback | `OnFallback` | `OnFallbackAsync` |
| Concurrency limit | `OnRejected` | `OnRejectedAsync` |
| Rate limit | `OnRejected` | `OnRejectedAsync` |
| Chaos | `OnInjected` | — |

The event payloads and timing are documented on each [strategy page](/docs/category/strategies). Metrics answer aggregate questions; callbacks add request-specific details.

## Logging and tracing

Kevlar does not create `ILogger` messages or `Activity` spans automatically. This avoids duplicate telemetry and keeps the core package independent of a logging provider or tracing SDK. Log callback event payloads with your application's `ILogger`, and create custom `Activity` events or spans in callbacks when strategy-level tracing is useful. The delegate executed by a shield runs in the caller's ambient `Activity`, so normal trace-context propagation continues through the protected operation.
