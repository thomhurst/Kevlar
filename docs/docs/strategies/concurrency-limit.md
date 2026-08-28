---
sidebar_position: 5
---

# Concurrency Limit

Concurrency isolation: cap how many executions run at once, so one misbehaving dependency can't drain your whole thread pool or connection pool.

See the [exceptions reference](../exceptions.md) for `ConcurrencyLimitExceededException`.

```csharp
Shield.ConcurrencyLimit(maxConcurrency: 10, queueLimit: 20);

Shield.ConcurrencyLimit(o =>
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

| Option | Default | What it does |
|---|---|---|
| `MaxConcurrency` | `10` | Executions allowed to run simultaneously |
| `QueueLimit` | `0` | Executions allowed to wait for a slot; `0` = reject immediately when all slots are busy |
| `OnRejected` | — | Awaited notification for an actual rejection; return `default` when the work is synchronous |

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
Shield.ConcurrencyLimit(10).Retry(3);   // concurrency limit wraps the retry loop:
                                //   one slot held for the WHOLE loop, delays included
Shield.Retry(3).ConcurrencyLimit(10);   // retry wraps the concurrency limit:
                                //   each attempt acquires (and releases) a slot
```

Retry-outside is usually what you want: slots are freed during backoff delays instead of being held while sleeping.

:::warning Sync callers block while queueing
With `QueueLimit > 0`, synchronous `Execute` waits on the semaphore with a blocking wait. Prefer async execution when queueing is enabled.
:::
