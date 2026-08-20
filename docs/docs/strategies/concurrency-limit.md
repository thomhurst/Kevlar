---
sidebar_position: 5
---

# Concurrency Limit

Concurrency isolation: cap how many executions run at once, so one misbehaving dependency can't drain your whole thread pool or connection pool.

```csharp
Shield.ConcurrencyLimit(maxConcurrency: 10, maxQueue: 20);

Shield.ConcurrencyLimit(o =>
{
    o.MaxConcurrency = 10;   // default 10
    o.MaxQueue = 20;         // default 0 — reject immediately when full
});
```

## Options

| Option | Default | What it does |
|---|---|---|
| `MaxConcurrency` | `10` | Executions allowed to run simultaneously |
| `MaxQueue` | `0` | Executions allowed to wait for a slot; `0` = reject immediately when all slots are busy |

Total capacity is `MaxConcurrency + MaxQueue`. Anything beyond that fails **immediately** with `ConcurrencyLimitExceededException` — the overflow check happens before any waiting, so rejection is instant and allocation-light.

## Why concurrency limits

This is the classic *bulkhead* pattern, named after ship compartments: a breach floods one compartment, not the hull. Give each downstream dependency its own concurrency-limited shield and a slow dependency saturates *its* 10 slots — while the rest of your service keeps breathing. Kevlar names the strategy for what it does rather than the metaphor.

```csharp
// Each dependency gets its own compartment:
var searchShield  = Shield.ConcurrencyLimit(10, maxQueue: 20).Timeout(TimeSpan.FromSeconds(2));
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
With `MaxQueue > 0`, synchronous `Execute` waits on the semaphore with a blocking wait. Prefer async execution when queueing is enabled.
:::
