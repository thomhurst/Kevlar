---
sidebar_position: 15
---

# Observability

Shields describe their configured pipeline, publish metrics through built-in `Meter` instances,
and expose strategy events for request-level telemetry. Use
[`Kevlar.Extensions.Logging`](logging.md) for structured `ILogger` events. Built-in analyzers catch
resilience mistakes at compile time. [Health checks](health-checks.md) expose registered circuit
breaker state and retained-partition summaries to readiness endpoints.

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

<div style={{overflowX: 'auto'}}>

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

</div>

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
its `Events` property and `WaitForEventCountAsync`. Core tracing is opt-in: no activities are
created unless a listener subscribes to the `Kevlar` activity source on .NET 8 or later.

### Built-in tracing

Subscribe to `KevlarDiagnostics.ActivitySourceName` (`"Kevlar"`) with your application's tracing
SDK. For OpenTelemetry, use `OpenTelemetry.Extensions.Hosting` and configure export as usual:

```csharp
using Kevlar;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(KevlarDiagnostics.ActivitySourceName));
```

The source creates internal spans with fixed names:

<div style={{overflowX: 'auto'}}>

| Span | Meaning | Tags |
| --- | --- | --- |
| `kevlar.execute` | One public shield execution, including empty pipelines and pre-cancelled calls | Optional `kevlar.shield.name`, `kevlar.execution.outcome`, optional `exception.type` |
| `kevlar.attempt` | One retry or hedge continuation invocation | Optional `kevlar.shield.name`, `kevlar.strategy.name`, `kevlar.strategy.index`, `kevlar.attempt.number`, `kevlar.attempt.outcome`, optional `exception.type` |

</div>

Attempt numbers are zero-based. Outcomes are `success`, `failure`, or `cancelled`. Failed and
cancelled spans have error status without an exception message or stack trace. Successful spans
keep unset status; the enclosing execution can succeed after a failed retry or losing hedge.
Names and exception types are limited to 256 UTF-16 code units without splitting surrogate pairs.
Results, bodies, operation keys, and arbitrary context properties are not copied. Keep names
bounded and non-sensitive: truncation limits size, not the number of distinct values or disclosure.

Retry attempts are children of the current execution. Concurrent hedge attempts are siblings;
nested retry/hedge strategies add nested attempt spans, and nested shield calls add execution
spans. Normal execution-context flow preserves parents across awaits. The caller's ambient
activity is restored before an incomplete operation is returned. A losing hedge can finish after
its execution span ends; its attempt span ends when that continuation finishes.

Strategy events use the `kevlar.` prefix, such as `kevlar.retry`, `kevlar.timeout`,
`kevlar.circuit_opened`, `kevlar.rejection`, and `kevlar.fallback`. Events enrich the nearest
active Kevlar ancestor, including when application or transport child activities are current.
They include strategy name/index, attempt number, outcome success, duration/delay in seconds,
and exception type when present. Circuit transitions add `kevlar.circuit.from`/`kevlar.circuit.to`;
rejections add `kevlar.rejection.kind` and optional `kevlar.retry_after.seconds`. Hedge-attempt
events carry `kevlar.hedge.winner` and `kevlar.hedge.cancelled`. Suppression uses
`kevlar.suppression.reason`. Events after all Kevlar ancestors have stopped are dropped.

Sampling belongs to the listener: `None` creates no span; propagation-only activities receive
no enrichment; `AllData` and `AllDataAndRecorded` receive tags/events. Tags are added after
sampling, so samplers select using source, span name, and parent context. No listener preserves
the allocation-free paths; enabled tracing allocates span/tag/event storage. The .NET Standard
asset exposes the source-name constant but creates no activities and adds no tracing dependency.
Listener start/sample/stop failures are isolated from protected outcomes. A global listener that
throws while the source is first constructed disables this source for the process.

### Enrich existing traces

Install `Kevlar.Extensions.Tracing` and keep one `KevlarTracing.Listen()` subscription for the
application lifetime. It adds `kevlar.strategy` events to the sampled `Activity.Current` at each
telemetry callback. It does not create spans, change their status, or require an OpenTelemetry SDK.
If the built-in `Kevlar` source is also enabled, the adapter enriches its current execution or attempt
span as well. Choose the source, the adapter, or both according to the telemetry shape you need;
enabling both produces both event schemas.

This runnable example configures an application-owned source and records a retry:

<!-- doc-test-run: tracing-enrichment -->
```csharp
using System.Diagnostics;
using Kevlar.Extensions.Tracing;

using var source = new ActivitySource("Catalog.Application");
using var listener = new ActivityListener
{
    ShouldListenTo = candidate => candidate == source,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
        ActivitySamplingResult.AllDataAndRecorded,
};
ActivitySource.AddActivityListener(listener);
using var tracing = KevlarTracing.Listen();
using var activity = source.StartActivity("load-catalog")!;
var attempts = 0;
var shield = Shield.Retry(1, Backoff.None).WithName("catalog");
var result = await shield.ExecuteAsync(async _ =>
{
    await Task.Yield();
    if (++attempts == 1)
    {
        throw new InvalidOperationException("Temporary failure");
    }
    return 42;
});

if (result != 42 || !activity.Events.Any(item => item.Tags.Any(tag =>
        tag.Key == "kevlar.event.name" && Equals(tag.Value, "retry"))))
{
    throw new InvalidOperationException("Expected a retry event on the application activity.");
}
```

In production, configure sampling and export in your tracing system. No matching source listener,
no current activity, propagation-only sampling, or an unrecorded/stopped activity means no enrichment.
Async retries and concurrent hedges follow `Activity.Current` through normal execution-context flow.
There is no saved context or activity lookup: suppressed flow loses correlation. Events occurring
outside an application activity are dropped. Transport attempt spans often stop before retry or
rejection callbacks run; keep an enclosing application activity alive across the entire shield call.
Late hedge-loser events after that activity stops are dropped, and concurrent event order is not guaranteed.

The fixed event name is `kevlar.strategy`. The event's tags use this bounded schema:

<div style={{overflowX: 'auto'}}>

| Tags | Meaning |
| --- | --- |
| `kevlar.event.name`, `kevlar.strategy.name`, `kevlar.strategy.index`, `kevlar.shield.name` | Event and pipeline identity |
| `kevlar.attempt.number`, `kevlar.outcome.success` | Zero-based attempt and its outcome |
| `kevlar.duration.seconds`, `kevlar.delay.seconds`, `kevlar.retry_after.seconds` | Measured duration, scheduled delay, or retry hint |
| `kevlar.circuit.from`, `kevlar.circuit.to` | Circuit transition, when available |
| `kevlar.hedge.winner`, `kevlar.hedge.cancelled` | Hedge-attempt selection and cancellation |
| `kevlar.rejection.kind`, `kevlar.suppression.reason` | Rejection or suppression classification |
| `kevlar.callback.kind`, `kevlar.callback.source` | Failed callback identity |
| `exception.type` | Exception type, without retaining the exception |

</div>

Optional tags are omitted when unavailable. String tag values are truncated to 256 UTF-16 code units,
without splitting surrogate pairs. Keep shield names, custom event names, and strategy names bounded
and non-sensitive. Truncation limits size; it does not redact data or bound the number of distinct values.
Results, request/response bodies, and arbitrary context properties are never copied. Event count follows
the telemetry stream; configure exporter limits and sampling for long-lived or high-volume activities.

`KevlarTracingOptions.IncludeOperationKey` opts into `kevlar.operation.key`.
`IncludeExceptionDetails` opts into `exception.message` and `exception.stacktrace`; those values may
contain sensitive data. `MaximumTagValueLength` changes the positive string limit. Options are copied
when subscribing, so later mutation has no effect. The adapter copies scalar values synchronously and
never retains pooled contexts, property bags, results, or exception objects.

Subscriptions are global and independent. Duplicate registrations produce duplicate events. Dispose the
subscription during shutdown; repeated or concurrent disposal is safe, though an in-flight callback can
finish after disposal. Listener failures cannot change execution outcomes. With no registration, the core
fast path is unchanged. A registration enables telemetry dispatch even with no sampled activity; the
adapter avoids tag creation and value-result boxing on that path. Recorded events allocate tag storage.
The package supports .NET Standard 2.0, .NET 8, and .NET 10; only the .NET Standard asset adds a
`System.Diagnostics.DiagnosticSource` package dependency.

### Custom tracing listeners

Keep a custom listener when you need a different schema, redaction policy, or correlation model.
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

See [Analyzer rules](analyzers.md) for the complete current rule set, rationale, safe alternatives,
conservative analysis limits, and zero-tolerance diagnostic guidance.

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

Add `Kevlar.Extensions.Logging` when structured `ILogger` strategy logs are useful. Subscribe to
the built-in `Kevlar` activity source for execution and attempt spans. Add
`Kevlar.Extensions.Tracing` for [events on existing sampled activities](#enrich-existing-traces),
or use custom listeners for a different schema. The core package requires no logging provider
or tracing SDK. Without a tracing listener, the protected delegate keeps the caller's ambient
`Activity`. With a listener, it runs inside the sampled execution or attempt activity, preserving
normal trace propagation.
