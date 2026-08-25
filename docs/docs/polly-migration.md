---
---

# Coming from Polly?

Kevlar and Polly v8 use the same pipeline model: the first strategy added is outermost, strategy
instances own their state, and executions flow through one immutable pipeline. The main migration
work is translating option names, defaults, context handling, and integrations.

Install the Kevlar packages that correspond to the Polly integrations you use:

```shell
dotnet add package Kevlar
dotnet add package Kevlar.Extensions.DependencyInjection
dotnet add package Kevlar.Extensions.Http
dotnet add package Kevlar.Extensions.Grpc
dotnet add package Kevlar.Extensions.RateLimiting
dotnet add package Kevlar.Testing
dotnet add package Kevlar.Chaos
```

## Pipelines and execution

Polly builds a pipeline from a builder:

```csharp
var pipeline = new ResiliencePipelineBuilder()
    .AddTimeout(TimeSpan.FromSeconds(10))
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    .Build();

await pipeline.ExecuteAsync(static _ => ValueTask.CompletedTask, cancellationToken);
```

Kevlar starts from a shorthand strategy or `Shield.Empty` and returns a new shield after every
addition:

```csharp
var migrationShield = Shield
    .Timeout(TimeSpan.FromSeconds(10))
    .Retry(3);

await migrationShield.ExecuteAsync(static _ => ValueTask.CompletedTask, cancellationToken);
```

| Polly v8 | Kevlar |
|---|---|
| `ResiliencePipeline` / `ResiliencePipeline<T>` | `Shield` / `Shield<T>` |
| `ResiliencePipeline.Empty` | `Shield.Empty` |
| `AddStrategy(strategy)` | `Use(strategy)` |
| `AddPipeline(pipeline)` | `Wrap` or `Compose` |
| `Execute` / `ExecuteAsync` | same names |
| `ExecuteOutcomeAsync` / `Outcome<T>` | same names; void work returns non-generic `Outcome` |
| `ResiliencePipelineBuilder.TimeProvider` | `WithTimeProvider(timeProvider)` |

Kevlar accepts both `Task`- and `ValueTask`-returning delegates. One `Shield` supports synchronous,
asynchronous, result-returning, and void executions; there is no separate sync or async type.

## Handling

Polly puts `ShouldHandle` on each reactive strategy:

```csharp
var handledPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 3,
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(static response => (int)response.StatusCode >= 500),
    })
    .Build();
```

Kevlar can express the predicate once as an ambient clause for later reactive strategies:

```csharp
var handledShield = Shield.For<HttpResponseMessage>()
    .When<HttpRequestException>()
    .OrResult(static response => (int)response.StatusCode >= 500)
    .Retry(3);
```

For a direct per-strategy translation, set `HandlesException` and `HandlesResult` on that
strategy's options. A local override replaces the ambient clause. `WhenAnyError()` returns later
strategies to Kevlar's default handling.

Polly predicates that inspect `args.AttemptNumber` or `args.Context.Properties` map to a
context-aware clause:

```csharp
var attemptAware = Shield
    .WhenContext(handling =>
        handling.Exception is TimeoutExceededException && handling.Attempt < 2)
    .Retry(3, Backoff.None);
```

`HandlingEvent.Attempt` is the direct zero-based counterpart to Polly's `AttemptNumber`.

Polly's default predicate handles every exception except `OperationCanceledException`. Kevlar also
lets execution-rejection exceptions and fatal runtime exceptions propagate by default. Use an
explicit clause when a shield intentionally recovers `CircuitOpenException`,
`RateLimitExceededException`, or `ConcurrencyLimitExceededException`.

## Context and properties

Polly callers rent and return `ResilienceContext` when they need properties or an operation key:

```csharp
var tenantProperty = new ResiliencePropertyKey<string>("tenant");
var resilienceContext = ResilienceContextPool.Shared.Get("catalog", cancellationToken);
resilienceContext.Properties.Set(tenantProperty, "north");
try
{
    await ResiliencePipeline.Empty.ExecuteAsync(
        static context => ValueTask.CompletedTask,
        resilienceContext);
}
finally
{
    ResilienceContextPool.Shared.Return(resilienceContext);
}
```

Kevlar pools `KevlarContext` automatically. Use `ExecuteWithContextAsync`, a `KevlarKey<T>`, and
`WithName`:

```csharp
var tenantKey = new KevlarKey<string>("tenant");
var contextShield = Shield.Empty.WithName("catalog");
await contextShield.ExecuteWithContextAsync(
    (tenantKey, tenant: "north"),
    static (state, properties) => properties.Set(state.tenantKey, state.tenant),
    static (state, context) =>
    {
        if (context.ShieldName != "catalog"
            || context.Properties.GetOrDefault(state.tenantKey, "missing") != state.tenant)
        {
            throw new InvalidOperationException("Context mapping failed.");
        }

        return ValueTask.CompletedTask;
    },
    cancellationToken);
```

| Polly v8 | Kevlar |
|---|---|
| `ResilienceContext` | `KevlarContext` |
| `ResilienceProperties` | `KevlarProperties` |
| `ResiliencePropertyKey<T>` | `KevlarKey<T>` |
| `OperationKey` | `WithName` / `KevlarContext.ShieldName` |
| `ExecuteAsync(callback, context)` | `ExecuteWithContextAsync(callback)` |
| `ContinueOnCapturedContext` | no equivalent; Kevlar library awaits do not capture the caller's context |

Do not retain either library's pooled context beyond the current callback or execution delegate.

Use the `onCompleted` overload of `ExecuteWithContextAsync` to copy final
`KevlarProperties` into caller-owned state before the context returns to the pool.

## Registry and dependency injection

Polly registers builders and resolves them through `ResiliencePipelineProvider<TKey>`:

```csharp
var pollyServices = new ServiceCollection();
pollyServices.AddResiliencePipeline("catalog", static builder => builder.AddRetry(
    new RetryStrategyOptions { MaxRetryAttempts = 3 }));
using var pollyProvider = pollyServices.BuildServiceProvider();
var registeredPipeline = pollyProvider
    .GetRequiredService<Polly.Registry.ResiliencePipelineProvider<string>>()
    .GetPipeline("catalog");
```

Kevlar registers built shields and resolves them through `IKevlarRegistry` or keyed services:

```csharp
using Kevlar.Extensions.DependencyInjection;

var kevlarServices = new ServiceCollection();
kevlarServices.AddShield("catalog", Shield.Retry(3));
using var kevlarProvider = kevlarServices.BuildServiceProvider();
var registeredShield = kevlarProvider
    .GetRequiredService<IKevlarRegistry>()
    .GetShield("catalog");
var keyedShield = kevlarProvider.GetRequiredKeyedService<Shield>("catalog");
```

`AddKevlar`, `AddShield`, `AddReloadingShield`, and [`AddPartitionedShield`](partitioning.md) cover fixed,
configuration-bound, reload-aware, and partitioned registrations. Dynamic registry operations map
as follows:

| Polly registry | Kevlar registry |
|---|---|
| `GetOrAddPipeline(key, ...)` | `IKevlarRegistry.GetOrAdd(name, factory)` |
| `TryAddBuilder(key, ...)` | `IKevlarRegistry.TryAdd(name, factory)` |
| remove a registry entry | `IKevlarRegistry.Remove(name)` |
| `EnableReloads<TOptions>()` | `AddReloadingShield<TOptions>(name, build)` using named `IOptionsMonitor<TOptions>` |

Kevlar's dynamic names are string-keyed and registry-only; keyed DI services still must be declared
before the service provider is built. Both providers expose non-throwing lookup forms.

## HTTP

The standard handlers have corresponding `IHttpClientBuilder` extensions:

```csharp
var pollyHttpServices = new ServiceCollection();
pollyHttpServices.AddHttpClient("catalog")
    .AddStandardResilienceHandler(options => options.Retry.MaxRetryAttempts = 3);
```

```csharp
using Kevlar.Extensions.Http;

var kevlarHttpServices = new ServiceCollection();
kevlarHttpServices.AddHttpClient("catalog")
    .AddStandardShield(options => options.Retry.MaxRetries = 3);
```

| Polly HTTP | Kevlar HTTP |
|---|---|
| `AddStandardResilienceHandler()` | `AddStandardShield()` |
| `AddStandardHedgingHandler()` | `AddStandardHedgeShield()` |
| `AddResilienceHandler(name, builder => …)` | build a `Shield<HttpResponseMessage>`, then `AddShield(shield)` |
| `SetResilienceContext` / request context properties | `WithKevlarProperties` / `KevlarHttp.GetRequestOptions(request)` |
| `ResilienceHandler(request => pipeline)` | `AddShield((request, serviceProvider) => shield)` |
| `HttpClientResiliencePredicates.IsTransient` | `HttpShield.IsTransient` |
| named gRPC resilience pipeline | `AddShieldUnaryInterceptor` / `AddShieldStreamingInterceptor` |

Kevlar buffers only bounded content and does not replay unsafe methods by default. Enable
`AllowUnsafeMethodReplay`, choose a bounded buffering policy, or supply a `RequestFactory` when a
POST-like request is intentionally replayable. See [safe request replay](http.md#safe-request-replay).
Per-request properties, shield overrides, replay opt-out, cancellation linking, selectors, and
request-keyed partitions are covered in [per-request options](http.md#per-request-options).
Use `WithKevlarProperties`, `WithShield`, `WithShieldName`, and
`WithKevlarCancellationToken` to configure the corresponding request options fluently;
`DisableReplay` opts out only that request.

## Telemetry

Polly can attach logging and telemetry configuration directly to a builder:

```csharp
var telemetryPipeline = new ResiliencePipelineBuilder()
    .ConfigureTelemetry(new Polly.Telemetry.TelemetryOptions())
    .AddRetry(new RetryStrategyOptions())
    .Build();
```

Kevlar publishes `System.Diagnostics.Metrics` instruments from the `Kevlar` meter:

```csharp
var telemetryServices = new ServiceCollection();
telemetryServices.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(KevlarDiagnostics.MeterName));
```

For Polly's `ConfigureTelemetry(loggerFactory)` logging behavior, add the logging package and
decorate a shield directly or register it once for every DI, reloading, and HTTP shield:

```csharp
using Kevlar.Extensions.Logging;

var loggedShield = Shield.Retry(3)
    .WithLogging(logger);

services.AddKevlarLogging();
```

Polly's `resilience.polly.strategy.events`, `resilience.polly.pipeline.duration`, and
`resilience.polly.attempt.duration` map to the instruments in the
[observability table](observability.md#metrics). Kevlar's `KevlarDiagnostics.Listen` is the
low-level event-stream equivalent; `Kevlar.Extensions.Logging` maps built-in events to stable
`EventId` values and structured fields.

## Testing

Both libraries expose immutable pipeline descriptions from dedicated testing packages:

```csharp
var pollyDescriptor = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions())
    .Build()
    .GetPipelineDescriptor();
if (pollyDescriptor.Strategies.Count != 1)
{
    throw new InvalidOperationException("Expected one Polly strategy.");
}
```

```csharp
using Kevlar.Testing;

var kevlarDescriptor = Shield.Retry()
    .GetDescriptor()
    .AssertStrategyCount(1)
    .AssertStrategyOrder(StrategyKind.Retry);
```

Use `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider` with either library. Kevlar.Testing
also supplies bounded `WaitForPendingAsync` and `AdvanceUntilAsync` helpers, state snapshots, and
`TelemetryRecorder`; see [testing your shields](testing.md).

## Chaos

Simmy strategies are part of Polly.Core:

```csharp
var chaosPipeline = new ResiliencePipelineBuilder()
    .AddChaosFault(1, static () => new IOException("injected"))
    .Build();
```

Kevlar's equivalent strategies live in `Kevlar.Chaos`:

```csharp
using Kevlar.Chaos;

var chaosMigrationShield = ChaosShield.Fault(options =>
{
    options.Enabled = true;
    options.InjectionRate = 1;
    options.Seed = 42;
    options.Exception = new IOException("injected");
});
```

| Simmy | Kevlar.Chaos |
|---|---|
| `AddChaosLatency` | `ChaosShield.Latency` |
| `AddChaosFault` | `ChaosShield.Fault` |
| `AddChaosOutcome` | `ChaosShield.Outcome` |
| `AddChaosBehavior` | `ChaosShield.Behavior` |
| custom `Randomizer` | fixed `Seed` |

## Defaults that differ

Defaults are not portable configuration. Set every value explicitly when identical behavior is
required.

| Strategy | Polly v8 default | Kevlar default |
|---|---|---|
| Retry | 3 retries; constant 2 s; no jitter | 3 retries; exponential from 250 ms; equal jitter; 30 s cap |
| Circuit breaker | failure ratio 0.1; minimum throughput 100; sampling 30 s; break 5 s | 5 consecutive failures; break 15 s; ratio mode uses minimum throughput 10 and sampling window 30 s |
| Hedging | 1 additional attempt (2 total); delay 2 s | 1 additional attempt (2 total); delay 1 s |
| Concurrency | permit limit 1,000; queue limit 0 | maximum concurrency 10; queue limit 0 |

The retry timing contract is executable. Polly waits exactly six seconds for three default retries;
Kevlar's equal jitter keeps each default exponential delay between half and one-and-a-half times its
base value.

<!-- doc-test-run: migration-retry-defaults -->
```csharp
using Kevlar.Testing;

var pollyClock = new FakeTimeProvider();
var pollyStartedAt = pollyClock.GetUtcNow();
var pollyAttempts = 0;
var defaultPollyRetry = new ResiliencePipelineBuilder
{
    TimeProvider = pollyClock,
}.AddRetry(new RetryStrategyOptions()).Build();
var pollyExecution = defaultPollyRetry.ExecuteAsync<int>(_ =>
{
    Interlocked.Increment(ref pollyAttempts);
    return ValueTask.FromException<int>(new InvalidOperationException());
}).AsTask();
await pollyExecution.WaitForPendingAsync(() => Volatile.Read(ref pollyAttempts) == 1, "Polly retry");
await pollyClock.AdvanceUntilAsync(
    TimeSpan.FromSeconds(2),
    () => Volatile.Read(ref pollyAttempts) == 4,
    "Polly's three default retries",
    maxAdvances: 3);
try { await pollyExecution; } catch (InvalidOperationException) { }
if (pollyAttempts != 4 || pollyClock.GetUtcNow() - pollyStartedAt != TimeSpan.FromSeconds(6))
{
    throw new InvalidOperationException("Polly retry defaults changed.");
}

var kevlarClock = new FakeTimeProvider();
var kevlarAttempts = 0;
var retryDelays = new TimeSpan[3];
var delayCount = 0;
var defaultKevlarRetry = Shield.Retry(options => options.OnRetry = retry =>
{
    retryDelays[delayCount++] = retry.Delay;
}).WithTimeProvider(kevlarClock);
var kevlarExecution = defaultKevlarRetry.ExecuteAsync<int>(_ =>
{
    Interlocked.Increment(ref kevlarAttempts);
    return ValueTask.FromException<int>(new InvalidOperationException());
}).AsTask();
for (var retryIndex = 0; retryIndex < 3; retryIndex++)
{
    await kevlarExecution.WaitForPendingAsync(
        () => Volatile.Read(ref delayCount) > retryIndex,
        $"Kevlar retry {retryIndex + 1}");
    kevlarClock.Advance(retryDelays[retryIndex]);
}
try { await kevlarExecution; } catch (InvalidOperationException) { }
var bases = new[] { 250d, 500d, 1000d };
if (kevlarAttempts != 4 || retryDelays.Where((delay, index) =>
        delay.TotalMilliseconds < bases[index] * 0.5 ||
        delay.TotalMilliseconds >= bases[index] * 1.5 ||
        delay > TimeSpan.FromSeconds(30)).Any())
{
    throw new InvalidOperationException("Kevlar retry defaults changed.");
}
```

The circuit timing and hedging count differences are executable too. Both clocks advance without
real waiting.

<!-- doc-test-run: migration-breaker-hedging-defaults -->
```csharp
using Kevlar.Testing;

var pollyBreakerClock = new FakeTimeProvider();
var pollyBreakerOptions = new CircuitBreakerStrategyOptions();
var pollyBreaker = new ResiliencePipelineBuilder
{
    TimeProvider = pollyBreakerClock,
}.AddCircuitBreaker(pollyBreakerOptions).Build();
for (var attempt = 0; attempt < 100; attempt++)
{
    try
    {
        await pollyBreaker.ExecuteAsync<int>(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException()));
    }
    catch (InvalidOperationException)
    {
    }
}
var pollyRejected = false;
try
{
    await pollyBreaker.ExecuteAsync<int>(static _ => new ValueTask<int>(1));
}
catch (BrokenCircuitException)
{
    pollyRejected = true;
}
pollyBreakerClock.Advance(TimeSpan.FromSeconds(5));
var pollyRecovered = await pollyBreaker.ExecuteAsync<int>(static _ => new ValueTask<int>(1));

var kevlarBreakerClock = new FakeTimeProvider();
var kevlarBreaker = Shield.CircuitBreaker(static _ => { }).WithTimeProvider(kevlarBreakerClock);
for (var attempt = 0; attempt < 5; attempt++)
{
    _ = await kevlarBreaker.ExecuteOutcomeAsync<int>(static _ =>
        ValueTask.FromException<int>(new InvalidOperationException()));
}
var kevlarRejection = await kevlarBreaker.ExecuteOutcomeAsync<int>(static _ => new ValueTask<int>(1));
kevlarBreakerClock.Advance(TimeSpan.FromSeconds(15));
var kevlarRecovered = await kevlarBreaker.ExecuteAsync<int>(static _ => new ValueTask<int>(1));
if (pollyBreakerOptions.FailureRatio != 0.1 || pollyBreakerOptions.MinimumThroughput != 100 ||
    pollyBreakerOptions.SamplingDuration != TimeSpan.FromSeconds(30) ||
    pollyBreakerOptions.BreakDuration != TimeSpan.FromSeconds(5) ||
    !pollyRejected || pollyRecovered != 1 ||
    kevlarRejection.Exception is not CircuitOpenException || kevlarRecovered != 1)
{
    throw new InvalidOperationException("Circuit-breaker defaults changed.");
}

var pollyHedgeClock = new FakeTimeProvider();
var pollyHedgeOptions = new HedgingStrategyOptions<int>();
var pollyHedge = new ResiliencePipelineBuilder<int>
{
    TimeProvider = pollyHedgeClock,
}.AddHedging(pollyHedgeOptions).Build();
var pollyPrimary = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
var pollyHedgeAttempts = 0;
var pollyHedgeExecution = pollyHedge.ExecuteAsync(_ =>
    Interlocked.Increment(ref pollyHedgeAttempts) == 1
        ? new ValueTask<int>(pollyPrimary.Task)
        : new ValueTask<int>(42)).AsTask();
await pollyHedgeExecution.WaitForPendingAsync(
    () => Volatile.Read(ref pollyHedgeAttempts) == 1,
    "Polly primary hedge");
await pollyHedgeClock.AdvanceUntilAsync(
    TimeSpan.FromSeconds(2),
    () => Volatile.Read(ref pollyHedgeAttempts) == 2,
    "Polly default hedge",
    maxAdvances: 1);
pollyPrimary.TrySetCanceled();
var pollyHedgeResult = await pollyHedgeExecution;

var kevlarHedgeClock = new FakeTimeProvider();
var kevlarHedgeOptions = new HedgeOptions();
var kevlarHedge = Shield.Hedge(static _ => { }).WithTimeProvider(kevlarHedgeClock);
var kevlarPrimary = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
var kevlarHedgeAttempts = 0;
var kevlarHedgeExecution = kevlarHedge.ExecuteAsync<int>(_ =>
    Interlocked.Increment(ref kevlarHedgeAttempts) == 1
        ? new ValueTask<int>(kevlarPrimary.Task)
        : new ValueTask<int>(42)).AsTask();
await kevlarHedgeExecution.WaitForPendingAsync(
    () => Volatile.Read(ref kevlarHedgeAttempts) == 1,
    "Kevlar primary hedge");
await kevlarHedgeClock.AdvanceUntilAsync(
    TimeSpan.FromSeconds(1),
    () => Volatile.Read(ref kevlarHedgeAttempts) == 2,
    "Kevlar default hedge",
    maxAdvances: 1);
kevlarPrimary.TrySetCanceled();
var kevlarHedgeResult = await kevlarHedgeExecution;

if (pollyHedgeOptions.MaxHedgedAttempts != 1 || pollyHedgeOptions.Delay != TimeSpan.FromSeconds(2) ||
    pollyHedgeAttempts != 2 || pollyHedgeResult != 42)
{
    throw new InvalidOperationException("Polly hedging defaults changed.");
}
if (kevlarHedgeOptions.MaxHedgedAttempts != 1 || kevlarHedgeOptions.Delay != TimeSpan.FromSeconds(1) ||
    kevlarHedgeAttempts != 2 || kevlarHedgeResult != 42)
{
    throw new InvalidOperationException("Kevlar hedging defaults changed.");
}
```

Finally, both default concurrency limiters execute a call while their configured permit and queue
limits remain an executable contract. Saturation behavior is covered by the integration suite.

<!-- doc-test-run: migration-concurrency-defaults -->
```csharp
var pollyLimiterOptions = new Polly.RateLimiting.RateLimiterStrategyOptions();
var pollyLimiter = new ResiliencePipelineBuilder().AddRateLimiter(pollyLimiterOptions).Build();
var pollyLimiterResult = await pollyLimiter.ExecuteAsync<int>(static _ => new ValueTask<int>(42));

var kevlarLimiterOptions = new ConcurrencyLimitOptions();
var kevlarLimiter = Shield.ConcurrencyLimit(static _ => { });
var kevlarLimiterResult = await kevlarLimiter.ExecuteAsync<int>(static _ => new ValueTask<int>(42));

if (pollyLimiterOptions.DefaultRateLimiterOptions.PermitLimit != 1000 ||
    pollyLimiterOptions.DefaultRateLimiterOptions.QueueLimit != 0 || pollyLimiterResult != 42 ||
    kevlarLimiterOptions.MaxConcurrency != 10 || kevlarLimiterOptions.QueueLimit != 0 ||
    kevlarLimiterResult != 42)
{
    throw new InvalidOperationException("Concurrency defaults changed.");
}
```

## Generators and callbacks

| Polly v8 | Kevlar |
|---|---|
| retry `DelayGenerator` returning `ValueTask<TimeSpan?>` | `DelayGenerator` plus `DelayGeneratorAsync` |
| `TimeoutGenerator` | `TimeoutGenerator` |
| hedging `ActionGenerator` | `HedgeActionGenerator.Create<T>` |
| circuit `BreakDurationGenerator` | `BreakDurationGenerator` |
| hedging `DelayGenerator` | `HedgeOptions.DelayGenerator` or `DelayGeneratorAsync`; `HedgeDelayEvent` exposes `AttemptNumber`, `Context`, and `Elapsed` |
| `OnOpened` / `OnClosed` / `OnHalfOpened` | one `OnStateChanged` callback |

Polly's `CircuitBreakerManualControl` and `CircuitBreakerStateProvider` are separately shareable.
Kevlar uses one `CircuitBreakerMonitor`, attached one-to-one to a breaker strategy.

## Rate limiting

Polly's `AddRateLimiter(new SlidingWindowRateLimiter(...))` maps to the
`Kevlar.Extensions.RateLimiting` adapter's `UseRateLimiter` extension. Polly throws
`RateLimiterRejectedException`; the Kevlar adapter throws `RateLimiterAdapterRejectedException`.
Core `Shield.RateLimit` throws `RateLimitExceededException` and remains Kevlar's
allocation-conscious token-bucket implementation, while the adapter
accepts `RateLimiter`, `PartitionedRateLimiter<KevlarContext>`, or a custom lease acquirer.

## Semantic differences

- **Ambient clauses.** One Kevlar clause applies to later reactive strategies until replaced.
  `Wrap` and `Compose` seal clauses at composition boundaries.
- **Fallback ordering fails fast.** Kevlar rejects a fallback placed inside a retry, hedge, or
  breaker when both share the same clause; Polly builds that ineffective order silently.
- **Unhandled circuit outcomes are neutral.** Current Kevlar, like Polly, neither records an
  unhandled exception as a breaker failure nor resets prior consecutive failures.
- **State is by instance.** Both libraries share breaker and limiter state only when the same built
  pipeline or shield instance is reused. `Wrap` and `Compose` reference rather than copy state.
- **Timeout exceptions are library-specific.** `TimeoutRejectedException` maps to
  `TimeoutExceededException`; neither derives from `System.TimeoutException`.
- **Circuit rejection details differ.** `BrokenCircuitException` maps to `CircuitOpenException`;
  `IsolatedCircuitException` maps to `CircuitOpenException.IsIsolated`.

The [exceptions reference](exceptions.md) lists every Kevlar rejection type and its properties.

## Polly v7

Classic Polly v7 concepts translate as follows:

| Polly v7 | Kevlar |
|---|---|
| `Policy.Handle<T>().WaitAndRetryAsync(...)` | `Shield.When<T>().Retry(...)` |
| `PolicyWrap` | `Wrap` / `Compose` |
| `Policy.BulkheadAsync(max, queue)` | `Shield.ConcurrencyLimit(max, queueLimit: queue)` |
| `TimeoutStrategy.Optimistic` | Kevlar timeouts; delegates must observe the supplied token |
| `TimeoutStrategy.Pessimistic` | no equivalent; Kevlar never abandons a still-running delegate |
| `Polly.Contrib.WaitAndRetry` helpers | `Backoff.Constant`, `Linear`, `Exponential`, or `Custom` |

For example, this v7 shape:

```text
Policy.Handle<HttpRequestException>()
    .WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(retry));
```

becomes:

```csharp
var v7MigrationShield = Shield
    .When<HttpRequestException>()
    .Retry(3, Backoff.Linear(TimeSpan.FromSeconds(1)));
```

Port pessimistic timeouts by making the dependency cancellation-aware or isolating it outside the
process; a shield cannot safely stop arbitrary synchronous work.
