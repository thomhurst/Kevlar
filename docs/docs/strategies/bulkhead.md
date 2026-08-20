---
sidebar_position: 5
---

# Bulkhead

Concurrency isolation: cap how many executions run at once, so one misbehaving dependency can't drain your whole thread pool or connection pool.

```csharp
Policy.Bulkhead(maxConcurrency: 10, maxQueue: 20);

Policy.Bulkhead(o =>
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

Total capacity is `MaxConcurrency + MaxQueue`. Anything beyond that fails **immediately** with `BulkheadRejectedException` — the overflow check happens before any waiting, so rejection is instant and allocation-light.

## Why bulkheads

The name comes from ship compartments: a breach floods one compartment, not the hull. Give each downstream dependency its own bulkhead-guarded policy and a slow dependency saturates *its* 10 slots — while the rest of your service keeps breathing.

```csharp
// Each dependency gets its own compartment:
var searchPolicy  = Policy.Bulkhead(10, maxQueue: 20).Timeout(TimeSpan.FromSeconds(2));
var paymentPolicy = Policy.Bulkhead(5).Timeout(TimeSpan.FromSeconds(10));
```

As with all stateful strategies, the slots live with the policy instance — share the instance across every call site of the dependency ([state-sharing rule](../composition.md#the-state-sharing-rule)).

## Placement

Bulkheads are proactive: they don't consult [handling clauses](../handling-failures.md). Remember the first strategy is outermost, so ordering decides who holds a slot for how long:

```csharp
Policy.Bulkhead(10).Retry(3);   // bulkhead wraps the retry loop:
                                //   one slot held for the WHOLE loop, delays included
Policy.Retry(3).Bulkhead(10);   // retry wraps the bulkhead:
                                //   each attempt acquires (and releases) a slot
```

Retry-outside is usually what you want: slots are freed during backoff delays instead of being held while sleeping.

:::warning Sync callers block while queueing
With `MaxQueue > 0`, synchronous `Execute` waits on the semaphore with a blocking wait. Prefer async execution when queueing is enabled.
:::
