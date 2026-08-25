---
sidebar_position: 15
---

# Testing your shields

Install `Kevlar.Testing` in the test project. It provides deterministic time helpers, immutable
pipeline and state snapshots, assertion helpers, and a telemetry recorder.

```shell
dotnet add package Kevlar.Testing
```

This page covers tests that consume Kevlar. Repository build, coverage, mutation, documentation,
and package-publishing checks belong in the
[contributor guide](https://github.com/thomhurst/Kevlar/blob/main/CONTRIBUTING.md).

## Deterministic time with TimeProvider

Every Kevlar delay, timeout, break duration, sampling window, rate-limit window, and hedge delay
uses a `TimeProvider`. Start the execution, wait until it has scheduled work, then advance its
`FakeTimeProvider`. The test below performs three attempts and advances twenty seconds without
sleeping.

`FakeTimeProvider`, `WaitForPendingAsync`, and `AdvanceUntilAsync` require a test project targeting
.NET 8 or later; they are not included in the `netstandard2.0` compatibility asset.

<!-- doc-test-run: testing-fake-time-retry -->
```csharp
using Kevlar.Testing;

var timeProvider = new FakeTimeProvider();
var startedAt = timeProvider.GetUtcNow();
var attempts = 0;
var shield = Shield
    .Retry(2, Backoff.Constant(TimeSpan.FromSeconds(10)))
    .WithTimeProvider(timeProvider);

var execution = shield.ExecuteAsync<int>(_ =>
{
    var attempt = Interlocked.Increment(ref attempts);
    return attempt < 3
        ? ValueTask.FromException<int>(new InvalidOperationException())
        : new ValueTask<int>(42);
}).AsTask();

await execution.WaitForPendingAsync(
    () => Volatile.Read(ref attempts) == 1,
    "the first retry delay");
await timeProvider.AdvanceUntilAsync(
    TimeSpan.FromSeconds(10),
    () => Volatile.Read(ref attempts) == 3,
    "all retry attempts",
    maxAdvances: 2);

var result = await execution;
if (result != 42 || attempts != 3 || timeProvider.GetUtcNow() - startedAt != TimeSpan.FromSeconds(20))
{
    throw new InvalidOperationException("The retry did not follow the expected fake-time schedule.");
}
```

`WaitForPendingAsync` and `AdvanceUntilAsync` are bounded. Their exceptions report the condition,
task status or fake UTC time, and the exhausted yield or advance count. Use only caller-owned
state—such as an attempt counter, callback flag, or deadline—as the condition.

Advancing before the execution starts cannot satisfy a delay that has not been scheduled. This
negative example uses a one-second test timeout to prove the task remains pending, then cancels it
so the executable documentation check cannot leak work.

<!-- doc-test-run: testing-advance-before-schedule -->
```csharp
#pragma warning disable CA2007 // Negative example deliberately uses Task.WhenAny as a test timeout.
using Kevlar.Testing;

var timeProvider = new FakeTimeProvider();
var attempts = 0;
using var cancellation = new CancellationTokenSource();
var shield = Shield
    .Retry(1, Backoff.Constant(TimeSpan.FromSeconds(10)))
    .WithTimeProvider(timeProvider);

timeProvider.Advance(TimeSpan.FromSeconds(10)); // Too early: no retry delay exists yet.
var execution = shield.ExecuteAsync<int>(_ =>
{
    Interlocked.Increment(ref attempts);
    return ValueTask.FromException<int>(new InvalidOperationException());
}, cancellation.Token).AsTask();

await execution.WaitForPendingAsync(
    () => Volatile.Read(ref attempts) == 1,
    "the retry delay scheduled after the early advance");
var timeout = Task.Delay(TimeSpan.FromSeconds(1));
if (!ReferenceEquals(await Task.WhenAny(execution, timeout), timeout))
{
    throw new InvalidOperationException("The retry unexpectedly completed without another advance.");
}

cancellation.Cancel();
try
{
    await execution;
}
catch (OperationCanceledException)
{
    // Expected cleanup after proving the task stayed pending.
}
#pragma warning restore CA2007
```

`WithTimeProvider` returns a new immutable shield. Copies share stateful strategy instances;
circuit breakers and rate limiters normalize different providers onto one monotonic elapsed-time
timeline. Advance time through the provider—changing UTC alone does not advance strategy windows.

## Asserting pipeline shape

`GetDescriptor()` returns an immutable snapshot. Assert stable strategy kinds, order, and typed
configuration properties; do not parse `Description`, which is diagnostic text.

<!-- doc-test-run: testing-pipeline-shape -->
```csharp
using Kevlar.Testing;

var shield = Shield
    .Timeout(TimeSpan.FromSeconds(2))
    .Retry(3, Backoff.Constant(TimeSpan.FromMilliseconds(50)))
    .WithName("catalog");

var descriptor = shield.GetDescriptor()
    .AssertStrategyCount(2)
    .AssertStrategyOrder(StrategyKind.Timeout, StrategyKind.Retry);
var retry = descriptor.AssertContainsSingle<RetryStrategyDescriptor>();

if (retry.MaxRetries != 3 || descriptor.Name != "catalog")
{
    throw new InvalidOperationException("The pipeline descriptor did not match the test contract.");
}
```

`AssertContains<TDescriptor>()` allows repeated strategies;
`AssertContainsSingle<TDescriptor>()` rejects duplicates. Shape failures throw
`ShieldAssertionException` with expected and actual details.

For live state, `GetStateSnapshot()` reports immutable circuit-breaker, rate-limiter, and
concurrency-limiter observations. Stateless strategies are omitted, and a snapshot is not a
reservation: another execution may change the strategy immediately after capture.

<!-- doc-test-run: testing-state-snapshot -->
```csharp
using Kevlar.Testing;

var shield = Shield.ConcurrencyLimit(1, queueLimit: 1);
await shield.ExecuteAsync(static _ => ValueTask.CompletedTask);

var state = shield.GetStateSnapshot();
var limiter = state.Strategies.OfType<ConcurrencyLimitStateSnapshot>().Single();
if (state.ContractVersion != 1 || limiter.AvailablePermits != 1)
{
    throw new InvalidOperationException("The limiter did not return to its idle state.");
}
```

## Recording telemetry

`TelemetryRecorder` captures immutable callback and metric snapshots without retaining a pooled
`KevlarContext`. Its `Record` overloads can be assigned directly to retry, fallback, timeout,
hedge, and circuit-transition callbacks.

<!-- doc-test-run: testing-telemetry-recorder -->
```csharp
using Kevlar.Testing;

using var telemetry = new TelemetryRecorder(captureMetrics: false);
var shield = Shield.Retry(options =>
{
    options.MaxRetries = 1;
    options.Backoff = Backoff.None;
    options.OnRetry = telemetry.Record;
}).WithName("catalog");

await shield.ExecuteOutcomeAsync<int>(static _ =>
    ValueTask.FromException<int>(new InvalidOperationException("offline")));
await telemetry.WaitForCallbackCountAsync(1);

var retry = telemetry.Callbacks.Single();
if (retry.Kind != CallbackKind.Retry || retry.RetryNumber != 1 || retry.ShieldName != "catalog")
{
    throw new InvalidOperationException("The expected retry callback was not recorded.");
}
```

Callback recording works from the `netstandard2.0` asset. Metric capture needs the .NET 8 or
.NET 10 `Kevlar.Testing` asset because `MeterListener` is unavailable in the compatibility asset.
Use `captureMetrics: false` for callback-only tests, and always dispose the recorder to detach its
listener. `WaitForMetricCountAsync` and `WaitForCallbackCountAsync` avoid polling concurrent tests.

## Testing HTTP shields

Test `Retry-After` with the same fake clock used by the shield. This example returns HTTP 429 once,
asserts that the server's three-second delay is observed, then succeeds.

<!-- doc-test-run: testing-http-retry-after -->
```csharp
using Kevlar.Extensions.Http;
using Kevlar.Testing;

var timeProvider = new FakeTimeProvider();
var startedAt = timeProvider.GetUtcNow();
var attempts = 0;
HttpResponseMessage? rejectedResponse = null;
var shield = HttpShield.WhenTransient()
    .Retry(options =>
    {
        options.MaxRetries = 1;
        options.Backoff = Backoff.None;
        options.DelayGenerator = HttpShield.RetryAfter(TimeSpan.FromSeconds(5));
    })
    .WithTimeProvider(timeProvider);

var execution = shield.ExecuteAsync(_ =>
{
    if (Interlocked.Increment(ref attempts) == 1)
    {
        rejectedResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rejectedResponse.Headers.RetryAfter =
            new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
        return new ValueTask<HttpResponseMessage>(rejectedResponse);
    }

    return new ValueTask<HttpResponseMessage>(new HttpResponseMessage(HttpStatusCode.OK));
}).AsTask();

await execution.WaitForPendingAsync(
    () => Volatile.Read(ref attempts) == 1,
    "the Retry-After delay");
await timeProvider.AdvanceUntilAsync(
    TimeSpan.FromSeconds(3),
    () => Volatile.Read(ref attempts) == 2,
    "the retried HTTP operation",
    maxAdvances: 1);

using var response = await execution;
rejectedResponse?.Dispose();
if (response.StatusCode != HttpStatusCode.OK || attempts != 2 ||
    timeProvider.GetUtcNow() - startedAt != TimeSpan.FromSeconds(3))
{
    throw new InvalidOperationException("Retry-After was not honored.");
}
```

For handler-level replay tests, use a recording `HttpMessageHandler`. Return a transient response,
then success; assert two sends, distinct request objects, and preserved method, URI, headers,
options, and content. A POST or one-shot body should remain single-send unless the test explicitly
sets `AllowUnsafeMethodReplay`, selects bounded buffering, or supplies a `RequestFactory`. See
[safe request replay](http.md#safe-request-replay) for the ownership rules.

## Testing partitioned shields

Trip one partition and prove another still succeeds. This tests isolation through public behavior
instead of inspecting cache internals.

<!-- doc-test-run: testing-partition-isolation -->
```csharp
var partitions = new PartitionedShield<string>(_ =>
    Shield.CircuitBreaker(
        consecutiveFailures: 1,
        breakDuration: TimeSpan.FromMinutes(1)));
var north = partitions.GetShield("north");
var south = partitions.GetShield("south");

var failure = await north.ExecuteOutcomeAsync<int>(static _ =>
    ValueTask.FromException<int>(new InvalidOperationException("north failed")));
var rejected = await north.ExecuteOutcomeAsync<int>(static _ => new ValueTask<int>(1));
var southResult = await south.ExecuteAsync(static _ => new ValueTask<int>(42));

if (failure.Exception is not InvalidOperationException ||
    rejected.Exception is not CircuitOpenException || southResult != 42 || partitions.Count != 2)
{
    throw new InvalidOperationException("Partition state was not isolated.");
}
```

Use the same key to test shared state and different keys to test isolation. Capacity and idle
expiration tests can assert `Count`, `CreatedCount`, `CapacityEvictionCount`, and
`ExpirationEvictionCount`; advance the configured fake clock before calling `PruneExpired()`.

## Chaos in tests

Chaos strategies are disabled by default. In a test, enable a 100% injection rate and fixed seed so
the expected fault is deterministic and the dependency delegate cannot run.

<!-- doc-test-run: testing-chaos-fault -->
```csharp
using Kevlar.Chaos;

var dependencyCalls = 0;
var shield = ChaosShield.Fault(options =>
{
    options.Enabled = true;
    options.InjectionRate = 1;
    options.Seed = 42;
    options.Exception = new IOException("injected");
});

var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
{
    Interlocked.Increment(ref dependencyCalls);
    return new ValueTask<int>(42);
});

if (outcome.Exception is not IOException { Message: "injected" } || dependencyCalls != 0)
{
    throw new InvalidOperationException("The deterministic chaos fault was not injected.");
}
```

Prefer a separate seeded shield per scenario: concurrent callers can consume one shield's random
sequence in different orders. Keep production chaos opt-in and bounded; broader rollout guidance
is on the [chaos engineering](chaos.md) page.
