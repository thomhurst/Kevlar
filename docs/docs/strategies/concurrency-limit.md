---
sidebar_position: 5
---

# Concurrency Limit

Concurrency isolation: cap how many executions run at once, so one misbehaving dependency can't drain your whole thread pool or connection pool.

See the [exceptions reference](../exceptions.md) for `ConcurrencyLimitExceededException`.

```csharp
var fixedLimit = Shield.ConcurrencyLimit(maxConcurrency: 10, queueLimit: 20);

var configuredLimit = Shield.ConcurrencyLimit(o =>
{
    o.MaxConcurrency = 10;   // default 10
    o.QueueLimit = 20;         // default 0 — reject immediately when full
    o.OnRejected = rejection =>
    {
        logger.LogWarning("Concurrency limit {Limit} rejected work", rejection.MaxConcurrency);
        return default;
    };
});
```

## Options

API reference: [`ConcurrencyLimitOptions`](pathname:///api/Kevlar.ConcurrencyLimitOptions.html).

<div style={{overflowX: 'auto'}}>

| Option | Default | What it does |
|---|---|---|
| `MaxConcurrency` | `10` | Executions allowed to run simultaneously |
| `QueueLimit` | `0` | Executions allowed to wait for a slot; `0` = reject immediately when all slots are busy |
| `OnRejected` | — | Awaited notification for an actual rejection; return `default` when the work is synchronous |

</div>

Invalid option values throw [`KevlarConfigurationException`](../exceptions.md#configuration-failures)
and identify the options type, property, and offending value.

Total capacity is `MaxConcurrency + QueueLimit`. Anything beyond that fails **immediately** with `ConcurrencyLimitExceededException` — the overflow check happens before any waiting, so rejection is instant and allocation-light.

For an actual rejection, Kevlar records the rejection counter, awaits `OnRejected`, then surfaces
`ConcurrencyLimitExceededException`. Observable limiter gauges report the current state at the next
metrics collection. The event includes the configured concurrency/queue limits, strategy index, and
`KevlarContext`. Under the shared [callback-failure contract](../observability.md#callback-failures),
failures are reported through `KevlarDiagnostics.OnCallbackError`, and
`ConcurrencyLimitExceededException` remains the rejection outcome. A hook that completes
synchronously works with synchronous `Execute`; one that yields throws `NotSupportedException`
there and must run through `ExecuteAsync`.

Callback contexts are pooled. Do not retain `ConcurrencyLimitRejectedEvent.Context` after the
returned `ValueTask` completes. Hooks run outside limiter locks and may run concurrently or
re-enter the same shield; captured state must be thread-safe.

Cancelling a queued execution frees its queue place when the asynchronous wait observes cancellation. `CancellationTokenSource.Cancel()` can return before that continuation updates accounting, so await the cancelled execution before assuming the place is reusable. If cancellation races a slot grant, the wait either cancels or acquires the slot; both paths update queue and running accounting exactly once, so later executions see the full capacity after the admitted work drains.

Queued cancellation is not rejection and invokes neither rejection hook. A pre-cancelled caller is
stopped at the shield boundary before the limiter runs.

`ConcurrencyLimit` has no queue timeout. To bound time spent waiting for a slot, compose a timeout
outside it: `Shield.Timeout(queueBudget).ConcurrencyLimit(maxConcurrency, queueLimit: queueLimit)`.

## Adaptive concurrency

Use `AdaptiveConcurrencyLimitOptions` when downstream capacity changes over time. The separate
options overload creates an AIMD (additive increase, multiplicative decrease) limiter:

```csharp
var adaptive = Shield.ConcurrencyLimit(new AdaptiveConcurrencyLimitOptions
{
    Algorithm = AdaptiveConcurrencyLimitAlgorithm.Aimd,
    MinLimit = 2,
    InitialLimit = 10,
    MaxLimit = 100,
    SamplingWindow = TimeSpan.FromSeconds(1),
    DecreaseFactor = 0.9,
    LatencyTolerance = 2,
}).Timeout(TimeSpan.FromSeconds(5));
```

The same overload is available on typed shields and handling-clause builders. Options are copied
when the shield is built. Share the shield across calls to the same dependency; derived views and
composed shields share its adaptive state, just like a static limiter.

The first completed sample starts the observation window. A completion at or beyond the sampling
interval evaluates all collected observations once, then starts a new window. There is no background
timer, no adjustment on every call, and no repeated growth for elapsed empty windows:

- A downstream exception, timeout, or rejection outcome makes the window congested. Caller
  cancellation is excluded. Rejections from this limiter itself do not indicate downstream failure
  and do not reduce the limit.
- Successful calls supply latency measured through the shield's `TimeProvider`. The first
  successful window establishes a baseline. A mean above `baseline × LatencyTolerance` is congested.
  Each successful window then updates the baseline with 10% weight for its mean, allowing it to
  follow lasting latency changes.
- Congestion sets the limit to `floor(current × DecreaseFactor)`, bounded by `MinLimit`.
- A healthy window increases the limit by one only if at least half its current permits were in
  use at some point, bounded by `MaxLimit`. Low traffic therefore does not inflate the limit.

The default range is 1–100, initial limit 10, sampling interval one second, decrease factor 0.9,
and latency tolerance 2. The algorithm is `Aimd`; other enum values are rejected. Limits must be
positive and ordered, the initial limit must be in range, the interval must be positive, the
decrease factor must be between zero and one exclusively, and latency tolerance must be finite
and at least one. AIMD follows the additive-increase/multiplicative-decrease control pattern;
Kevlar applies its own windowed latency feedback rather than reproducing another library's controller.
See [Netflix's AIMD implementation](https://github.com/Netflix/concurrency-limits/blob/main/concurrency-limits-core/src/main/java/com/netflix/concurrency/limits/limit/AIMDLimit.java)
for the underlying control pattern.

Adaptive admission has no wait queue. Excess callers receive `ConcurrencyLimitExceededException`;
optional `OnRejected` callbacks follow the same contract as the static limiter, with
`MaxConcurrency` reporting the limit at rejection. Reducing a limit does not cancel existing calls:
they drain normally, and new admission waits until the running count falls below the new limit.
Place timeouts inside the limiter, as above, so it sees a timeout exception. An outer timeout's
cancellation reaches the limiter as caller cancellation and is excluded from feedback. Returned
results are never changed by adaptation, and result-valued failures require downstream code to
represent them as exceptions for this controller.

`Kevlar.Testing` exposes the current limit as `ConcurrencyLimitStateSnapshot.CurrentLimit` and
the immutable settings as `AdaptiveConcurrencyLimitStrategyDescriptor`. Running executions can
temporarily exceed `CurrentLimit` after a reduction; `AvailablePermits` remains zero until they drain.
On the `net10.0` asset, `kevlar.concurrency_limit.limit` reports the current limit, while
`kevlar.concurrency_limit.capacity` reports the configured maximum. Static limiters report the same
value for both. Snapshots and adaptation work on every supported core target framework.

## Why concurrency limits

This is the classic *bulkhead* pattern, named after ship compartments: a breach floods one compartment, not the hull. Give each downstream dependency its own concurrency-limited shield and a slow dependency saturates *its* 10 slots — while the rest of your service keeps breathing. Kevlar names the strategy for what it does rather than the metaphor.

```csharp
// Each dependency gets its own compartment:
var searchShield  = Shield.ConcurrencyLimit(10, queueLimit: 20).Timeout(TimeSpan.FromSeconds(2));
var paymentShield = Shield.ConcurrencyLimit(5).Timeout(TimeSpan.FromSeconds(10));
```

As with all stateful strategies, the slots live with the shield instance — share the instance across every call site of the dependency ([state-sharing rule](../composition.md#the-state-sharing-rule)).

## Placement

Concurrency limits are proactive: they don't consult [handling clauses](../handling-failures.md). Remember the first strategy is outermost, so ordering decides who holds a slot for how long:

```csharp
var loopLimited = Shield.ConcurrencyLimit(10).Retry(3); // limit wraps retry loop:
                                //   one slot held for the WHOLE loop, delays included
var attemptLimited = Shield.Retry(3).ConcurrencyLimit(10); // retry wraps limit:
                                //   each attempt acquires (and releases) a slot
```

Retry-outside is usually what you want: slots are freed during backoff delays instead of being held while sleeping.

:::warning Sync callers block while queueing
With `QueueLimit > 0`, synchronous `Execute` waits on the semaphore with a blocking wait. Prefer async execution when queueing is enabled.
:::
