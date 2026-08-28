---
sidebar_position: 15
---

# Observability

Shields describe their configured pipeline, publish metrics through built-in `Meter` instances,
and expose strategy events for request-level telemetry. Use
[`Kevlar.Extensions.Logging`](logging.md) for structured `ILogger` events. Built-in analyzers catch
resilience mistakes at compile time.

## Pipeline descriptions

`shield.ToString()` prints the whole pipeline, outermost strategy first, with each strategy's configuration:

<!-- doc-test-run: pipeline-description -->
```csharp
using Kevlar;
using OpenTelemetry.Metrics;

var shield = Shield
    .Timeout(TimeSpan.FromSeconds(30))
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30))
    .ConcurrencyLimit(10, queueLimit: 5)
    .WithName("github");

Console.WriteLine(shield);
// github: Timeout(30s) → Retry(3, exponential 250ms ×2, equal jitter, cap 30s) → CircuitBreaker(5 consecutive, break 30s) → ConcurrencyLimit(10, queue 5)
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

Shields that use only strategy defaults print exactly as before, with no prefix. Retry, circuit
breaker, and hedge defaults exclude cancellation, Kevlar's fail-fast rejections, and fatal runtime
failures; fallback additionally handles fail-fast rejections.

## Metrics

Kevlar publishes core metrics through a `System.Diagnostics.Metrics.Meter` named `Kevlar`, version `1.0`. The core and runtime extension packages contain instrumented `net8.0` and `net10.0` assets; only `netstandard2.0` consumers receive inert metric implementations. `Kevlar.Chaos` separately publishes its injection counter from its `net8.0` and `net10.0` assets through a meter named `Kevlar.Chaos`, version `1.0`.

Register the core meter with OpenTelemetry:

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddOpenTelemetry().WithMetrics(metrics => metrics
    .AddMeter(KevlarDiagnostics.MeterName));
```

Applications that reference the optional `Kevlar.Chaos` package can register its meter separately:

```csharp
using Kevlar.Chaos;
using Microsoft.Extensions.DependencyInjection;

services.AddOpenTelemetry().WithMetrics(metrics => metrics
    .AddMeter(ChaosDiagnostics.MeterName));
```

| Instrument | Type | Unit | Minimum target | Measures | Attributes |
|---|---|---|---|---|---|
| `kevlar.executions` | Counter | `{execution}` | `net8.0` | completed public execution calls, including empty shields and pre-cancelled calls | `kevlar.shield.name`, `kevlar.execution.outcome` (`success`/`failure`) |
| `kevlar.retries` | Counter | `{retry}` | `net8.0` | retry attempts | `kevlar.shield.name` |
| `kevlar.timeouts` | Counter | `{timeout}` | `net8.0` | executions cancelled by a timeout strategy, including delegates that complete after ignoring cancellation | `kevlar.shield.name`, optional `outcome` (`ignored`) |
| `kevlar.hedges` | Counter | `{hedge}` | `net8.0` | extra hedged attempts launched | `kevlar.shield.name` |
| `kevlar.hedge_attempts` | Counter | `{attempt}` | `net8.0` | completed attempts within hedged executions | `kevlar.shield.name`, `result` (`won`/`lost`/`cancelled`/`failed`) |
| `kevlar.fallbacks` | Counter | `{fallback}` | `net8.0` | outcomes replaced by a fallback | `kevlar.shield.name` |
| `kevlar.rejections` | Counter | `{rejection}` | `net8.0` | fail-fast rejections | `kevlar.shield.name`, `kevlar.rejection.type` (`circuit_open`/`rate_limit`/`rate_limiter_adapter`/`concurrency_limit`) |
| `kevlar.http.replay_suppressed` | Counter | `{request}` | `net8.0` | HTTP requests whose configured additional attempts were disabled for replay safety | `kevlar.shield.name`, `kevlar.suppression.reason` (`replay_disabled`/`unsafe_method`/`non_replayable_content`) |
| `kevlar.circuit_breaker.transitions` | Counter | `{transition}` | `net8.0` | circuit state changes | `kevlar.circuit_breaker.state.from`, `kevlar.circuit_breaker.state.to` (`closed`/`open`/`half_open`/`isolated`) |
| `kevlar.partitions.evictions` | Counter | `{partition}` | `net8.0` | partitions removed from bounded providers | `kevlar.partition.reason` (`capacity`/`idle`/`cleared`) |
| `kevlar.callback_errors` | Counter | `{error}` | `net8.0` | exceptions thrown by strategy notifications, observers, or superseded-result disposal | `kevlar.shield.name`, `kevlar.callback.kind`, `kevlar.callback.source` |
| `kevlar.execution.duration` | Histogram | `s` | `net8.0` | completed public execution duration | `kevlar.shield.name`, `kevlar.execution.outcome` (`success`/`failure`) |
| `kevlar.strategy.events` | Counter | `{event}` | `net8.0` | built-in strategy and caller-recorded events | `kevlar.shield.name`, `kevlar.strategy.index`, `kevlar.strategy.name`, `kevlar.event.name`, `kevlar.event.severity`, `kevlar.attempt.number`, optional `exception.type`, optional `kevlar.operation.key`, optional `kevlar.suppression.reason` |
| `kevlar.attempt.duration` | Histogram | `ms` | `net8.0` | retry attempt duration, including the initial attempt | `kevlar.shield.name`, `kevlar.strategy.index`, `kevlar.strategy.name`, `kevlar.event.name`, `kevlar.event.severity`, `kevlar.attempt.number`, optional `exception.type`, optional `kevlar.operation.key` |
| `kevlar.circuit_breaker.state` | ObservableGauge | `{state}` | `net10.0` | current circuit state: closed `0`, open `1`, half-open `2`, isolated `3` | `kevlar.shield.name`, `kevlar.strategy.index` |
| `kevlar.circuit_breaker.instances` | ObservableGauge | `{circuit}` | `net10.0` | circuit-breaker instances grouped by current state | `kevlar.shield.name`, `kevlar.strategy.index`, `kevlar.circuit_breaker.state` (`closed`/`open`/`half_open`/`isolated`) |
| `kevlar.concurrency_limit.inflight` | ObservableGauge | `{execution}` | `net10.0` | executions holding a permit | `kevlar.shield.name`, `kevlar.strategy.index` |
| `kevlar.concurrency_limit.queued` | ObservableGauge | `{execution}` | `net10.0` | executions waiting for a permit | `kevlar.shield.name`, `kevlar.strategy.index` |
| `kevlar.concurrency_limit.capacity` | ObservableGauge | `{execution}` | `net10.0` | configured concurrency permit capacity | `kevlar.shield.name`, `kevlar.strategy.index` |
| `kevlar.rate_limit.available` | ObservableGauge | `{permit}` | `net10.0` | immediately available burst permits at collection time | `kevlar.shield.name`, `kevlar.strategy.index` |
| `kevlar.rate_limit.queued` | ObservableGauge | `{execution}` | `net10.0` | executions waiting for a rate-limit permit | `kevlar.shield.name`, `kevlar.strategy.index` |
| `kevlar.chaos.injections` | Counter | `{injection}` | `net8.0` | chaos injections applied | `kevlar.chaos.kind`, `kevlar.shield.name`, `kevlar.chaos.operation`, `kevlar.chaos.environment` |

Each public execution call records exactly one `kevlar.executions` measurement after its final outcome: recovery through fallback is `success`; exceptions, caller cancellation, timeout, and strategy rejection are `failure`. Retry and hedge attempts do not add execution measurements of their own.

The `kevlar.shield.name` attribute appears only for shields named via `WithName` — name the shields you plan to chart. `WithName("")` emits the attribute with an empty value; an unnamed shield omits it. Optional chaos scope attributes are also omitted when unset. Instrument and attribute names use the product-specific `kevlar` namespace; count units use singular UCUM annotations. Counters and the duration histogram are active in the native `net8.0` and `net10.0` assets; the shipped state gauges require `net10.0`. On `netstandard2.0` targets the instruments are inert because the metrics API is not available in-box.

Strategy event names and strategy names are stable bounded values. Exception telemetry uses only the
full exception type name; messages are never tags. To correlate a small fixed set of logical
operations, set `KevlarKeys.OperationKey` while initializing execution properties. Never put request
IDs, URLs, partition keys, tenant IDs, or other unbounded values in that key.
The `kevlar.attempt.number` metric attribute is capped at `63` to bound series cardinality; telemetry
listener events retain the exact attempt number.

Every strategy options type has an optional `Name`. When unset, telemetry uses the built-in strategy
name such as `Retry`; when set, the configured value becomes `kevlar.strategy.name`. Keep strategy
names bounded just like shield names.

Built-in `kevlar.event.name` values are `execution_attempt`, `retry`, `timeout`, `timeout_ignored`, `hedge`, `hedge_attempt`,
`fallback`, `rejection`, `circuit_opened`, `circuit_half_opened`, `circuit_closed`, and
`circuit_isolated`; the HTTP integration also emits `attempts_suppressed`. `Kevlar.Chaos`
additionally emits `chaos_latency`, `chaos_fault`,
`chaos_outcome`, and `chaos_behavior`.

### Metric enrichment

Register a `KevlarMetricEnricher` to append application-defined tags to every enabled instrument
on the core `Kevlar` meter. Enrichers run synchronously in registration order. Their exceptions are
ignored, and disposing the returned subscription removes the registration. `Context` is the active
`KevlarContext` for execution-bound measurements; it is `null` for measurements produced outside
an execution, including observable state collection, circuit transitions, and partition evictions.

<!-- doc-test-declaration: split-before=using var metricEnrichment -->
```csharp
sealed class RegionMetricEnricher : KevlarMetricEnricher
{
    public static KevlarKey<string> RegionKey { get; } = new("deployment-region");

    public override void Enrich(in KevlarMetricEnrichmentContext context)
    {
        if (context.Context?.Properties.TryGet(RegionKey, out string? region) == true)
        {
            context.Tags.Add(new("deployment.region", region));
        }
    }
}

using var metricEnrichment =
    KevlarDiagnostics.AddMetricEnricher(new RegionMetricEnricher());

await Shield.Empty.ExecuteWithContextAsync(
    "eu-west",
    static (region, properties) => properties.Set(RegionMetricEnricher.RegionKey, region),
    static (_, _) => ValueTask.CompletedTask);
```

Treat enriched dimensions as part of the metric schema. Use a fixed vocabulary of tag names and
bounded values such as deployment regions or workload classes. Never add request IDs, raw URLs,
user IDs, partition keys, exception messages, or other unbounded values. Add tags rather than
removing or replacing Kevlar's built-in tags. Enrichment applies only to the `Kevlar` meter;
`Kevlar.Chaos` uses its separate documented schema.

### Telemetry listener and custom events

`KevlarDiagnostics.Listen` provides the same events synchronously without requiring a metrics
backend. The callback receives a `KevlarTelemetryEvent` containing the active context, attempt
metadata, duration, and exception. The context and its properties are valid only during the callback.
Listener exceptions are ignored and cannot replace the execution outcome.

<!-- doc-test-declaration: split-before=using var subscription -->
```csharp
sealed class Listener : IKevlarTelemetryListener
{
    public void OnEvent(in KevlarTelemetryEvent item) =>
        Console.WriteLine($"{item.StrategyName}: {item.EventName}");
}

using var subscription = KevlarDiagnostics.Listen(new Listener());
```

Custom strategies can publish through the same listener and meter using
`context.RecordEvent("cache_refresh", strategyName: "CacheRefresh")`. Event names and strategy names must come from a bounded vocabulary; exception
messages and operation-specific data belong in logs, not metric dimensions.

`Kevlar.Testing.TelemetryRecorder` subscribes to this stream and exposes immutable snapshots through
its `Events` property and `WaitForEventCountAsync`. Kevlar intentionally does not create
`ActivitySource` spans: use the metrics and listener hook to enrich the tracing system already owned
by the application or transport.

For applications that want one span per Kevlar event, bridge the listener into an application-owned
`ActivitySource`. Keep tags bounded and copy everything needed during the callback because the
context is pooled:

<!-- doc-test-declaration: split-before=using var activitySource -->
```csharp
using System.Diagnostics;

sealed class KevlarActivityListener(ActivitySource source) : IKevlarTelemetryListener
{
    public void OnEvent(in KevlarTelemetryEvent item)
    {
        using var activity = source.StartActivity(
            $"kevlar.{item.EventName}",
            ActivityKind.Internal);
        activity?.SetTag("kevlar.shield.name", item.ShieldName);
        activity?.SetTag("kevlar.strategy.name", item.StrategyName);
        activity?.SetTag("kevlar.attempt.number", item.AttemptNumber);
    }
}

using var activitySource = new ActivitySource("Catalog.Resilience");
using var tracingSubscription = KevlarDiagnostics.Listen(new KevlarActivityListener(activitySource));
```

If the protected transport already creates a client span, prefer adding events or tags to that span
instead of creating a second overlapping duration span.

The state gauges are observable instruments sampled only when the metrics reader collects them. They read the strategies' existing synchronized state instead of publishing from execution paths, so enabling state metrics adds no state-publication locks or listener callbacks to each execution. Listener failures therefore remain confined to collection. Gauges carry a bounded `kevlar.strategy.index` attribute (the strategy's zero-based pipeline position), so independent stateful strategies in one named pipeline remain distinct. Concurrency and rate-limit measurements that share a shield name and strategy index are summed. `kevlar.circuit_breaker.instances` counts breakers by the bounded `kevlar.circuit_breaker.state` attribute; this gives partitioned shields meaningful per-state totals without adding partition keys. The numeric `kevlar.circuit_breaker.state` gauge remains a per-instance compatibility instrument and can produce duplicate series when several breakers share a name/index pair; use the instance-count gauge for that case. Shared strategies expose up to 64 observed name/index aliases; additional aliases are omitted to bound memory and series growth. Registrations hold strategy instances weakly and discard collected registrations during observation, so telemetry does not keep an abandoned shield alive.

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

Telemetry cost depends on the runtime, pipeline, listener, exporter, and deployment hardware. State-gauge collection may take strategy-internal synchronization while it reads a snapshot, but shield execution never takes a telemetry publication lock. See [Benchmarks](benchmarks.md) for the current methodology and generated results. Run `TelemetryBenchmarks` and `StateMetricsContentionBenchmarks` on deployment-class hardware to measure the cost in your environment.

## Compile-time checks

The `Kevlar` package includes compile-time checks for resilience mistakes automatically.

See [Analyzer rules](analyzers.md) for the complete current rule set, rationale, safe alternatives, conservative analysis limits, and suppression guidance.

## Callbacks

Strategy callbacks expose request-level events. Every hook returns `ValueTask` and is awaited before
execution continues. A hook that completes synchronously (`return default;`) costs nothing extra and
works with synchronous `Execute`; one that yields requires `ExecuteAsync` (see
[synchronous execution compatibility](executing.md#synchronous-execution-compatibility)).

| Strategy | Hook |
|---|---|
| Retry | `OnRetry` |
| Circuit breaker | `OnStateChanged` |
| Timeout | `OnTimeout` |
| Hedging | `OnHedge` |
| Fallback | `OnFallback` |
| Concurrency limit | `OnRejected` |
| Rate limit | `OnRejected` |
| Chaos | `OnInjected` |

The event payloads and timing are documented on each [strategy page](/docs/category/strategies). Metrics answer aggregate questions; callbacks add request-specific details.

### Callback failures

Notification, observer, and superseded-result disposal exceptions never replace the protected
operation's result, failure, timeout, rejection, or fallback. Kevlar awaits every callback, reports each exception
through `KevlarDiagnostics.OnCallbackError`, increments `kevlar.callback_errors`, and continues.
Each diagnostics subscriber is isolated too: one throwing subscriber cannot prevent later
subscribers from receiving the error.

`KevlarDiagnostics.OnCallbackError` is process-global, not scoped to a shield or dependency
injection container. Unsubscribe handlers when their lifetime ends, especially in tests, to avoid
cross-test callbacks and retained state.

This differs from Polly, where a strategy-hook exception propagates to the caller. During migration,
use `KevlarDiagnostics.OnCallbackError`, `AddKevlarLogging`, or `TelemetryRecorder` in tests to keep
hook failures visible. See [semantic differences](polly-migration.md#semantic-differences).

`CallbackErrorEvent` is detached from the pooled execution context. It carries the callback kind,
stable source, shield name, strategy index, and original exception, so it can safely be retained or
queued. Satellite integrations use `CallbackErrorKind.Custom` and identify their callback through
`Source`.

<!-- doc-test-run: callback-failures -->
```csharp
var errors = new List<CallbackErrorEvent>();
Action<CallbackErrorEvent> handler = errors.Add;
KevlarDiagnostics.OnCallbackError += handler;

try
{
    var attempts = 0;
    var shield = Shield.Retry(options =>
    {
        options.MaxRetries = 1;
        options.Backoff = Backoff.None;
        options.OnRetry = _ => throw new IOException("logger unavailable");
    }).WithName("catalog");

    var result = await shield.ExecuteAsync(_ =>
        new ValueTask<int>(++attempts == 1
            ? throw new HttpRequestException("transient")
            : 42));

    if (result != 42 || errors is not [{ Kind: CallbackErrorKind.Retry }])
    {
        throw new InvalidOperationException("Callback isolation was not observed.");
    }
}
finally
{
    KevlarDiagnostics.OnCallbackError -= handler;
}
```

## Logging and tracing

The core package does not create `ILogger` messages or `Activity` spans automatically. This avoids
duplicate telemetry and keeps it independent of a logging provider or tracing SDK. Add
`Kevlar.Extensions.Logging` when structured strategy logs are useful. Create custom `Activity`
events or spans in callbacks when strategy-level tracing is useful. The delegate executed by a
shield runs in the caller's ambient `Activity`, so normal trace-context propagation continues
through the protected operation.
