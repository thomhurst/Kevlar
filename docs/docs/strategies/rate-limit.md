---
sidebar_position: 4
---

# Rate Limit

A token-bucket limiter: `Permits` executions per `Window`, with bursts and optional queueing.

```csharp
Policy.RateLimit(100, perWindow: TimeSpan.FromSeconds(1));   // 100/s, burst = 100

Policy.RateLimit(o =>
{
    o.Permits = 100;                       // default 100
    o.Window = TimeSpan.FromSeconds(1);    // default 1s
    o.Burst = 200;                         // default: same as Permits
    o.QueueLimit = 20;                     // default 0
});
```

## Options

| Option | Default | What it does |
|---|---|---|
| `Permits` | `100` | Executions allowed per window |
| `Window` | `1s` | The replenishment window |
| `Burst` | = `Permits` | Bucket capacity: how far above the steady rate a burst may spike |
| `QueueLimit` | `0` | How many executions may *wait* for a permit instead of being rejected immediately |

## Rejection vs queueing

With `QueueLimit = 0`, an execution that finds the bucket empty fails immediately with `RateLimitExceededException`. The exception carries `RetryAfter` — an estimate of when a permit will next be available — which pairs naturally with an outer retry's `DelayGenerator`.

With `QueueLimit > 0`, up to that many executions reserve a future permit and **wait** for it instead of failing. Beyond the queue limit, rejections resume.

:::note Queueing is reservation-based, not FIFO
Queued executions each sleep until their reserved permit replenishes; there's no fairness ordering among waiters.
:::

## Placement and sharing

Rate limiting is proactive — it doesn't consult [handling clauses](../handling-failures.md); it acts on every execution that reaches it.

The bucket lives with the policy instance. Reuse one instance for everything hitting the limited dependency, or you'll have several independent buckets each allowing the full rate ([state-sharing rule](../composition.md#the-state-sharing-rule)).

```csharp
// Retry politely around the limiter: waits what the limiter suggests
var polite = Policy
    .Handle<RateLimitExceededException>()
    .Retry(o =>
    {
        o.MaxRetries = 3;
        o.DelayGenerator = e => (e.Exception as RateLimitExceededException)?.RetryAfter;
    })
    .RateLimit(100, TimeSpan.FromSeconds(1));
```

:::warning Sync callers block
In synchronous `Execute`, queued waits block the calling thread. Prefer `ExecuteAsync` for queue-enabled limiters.
:::
