---
sidebar_position: 4
---

# Rate Limit

A token-bucket limiter: `Permits` executions per `Window`, with bursts and optional queueing.

```csharp
Shield.RateLimit(100, perWindow: TimeSpan.FromSeconds(1));   // 100/s, burst = 100

Shield.RateLimit(o =>
{
    o.Permits = 100;                       // default 100
    o.Window = TimeSpan.FromSeconds(1);    // default 1s
    o.Burst = 200;                         // default: same as Permits
    o.QueueLimit = 20;                     // default 0
    o.OnRejected = rejection =>
        logger.LogWarning("Rate limited; retry after {RetryAfter}", rejection.RetryAfter);
    o.OnRejectedAsync = static _ => ValueTask.CompletedTask;
});
```

## System.Threading.RateLimiting adapters

Install `Kevlar.Extensions.RateLimiting` to reuse a framework limiter without adding that dependency
to Kevlar core:

```shell
dotnet add package Kevlar.Extensions.RateLimiting
```

```csharp
using Kevlar.Extensions.RateLimiting;
using System.Threading.RateLimiting;

using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
{
    PermitLimit = 100,
    Window = TimeSpan.FromSeconds(1),
    QueueLimit = 20,
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
});

var shield = Shield.Empty.RateLimit(limiter, options =>
{
    options.PermitCount = 1;
    options.OnRejected = rejection =>
        Console.WriteLine(rejection.RetryAfter);
});

await shield.ExecuteAsync(static _ => ValueTask.CompletedTask);
```

The caller owns the `RateLimiter`; the adapter never disposes it. Every returned `RateLimitLease`
is held until the protected execution completes and is then disposed exactly once. Rejected lease
metadata is copied before disposal. `MetadataName.RetryAfter` becomes
`RateLimitExceededException.RetryAfter`, and the complete immutable snapshot is available from
`RateLimiterRejectedEvent.Metadata`.

Fixed-window, sliding-window, concurrency, chained, and custom limiters all use the same adapter.
For a limiter owned behind another abstraction, supply asynchronous acquisition directly:

<!-- doc-test-ignore: AcquireTenantLeaseAsync is supplied by the application's limiter abstraction. -->
```csharp
var shield = Shield.Empty.RateLimit(
    static (permitCount, context) =>
        AcquireTenantLeaseAsync(permitCount, context.CancellationToken));
```

Use `PartitionedRateLimiter<KevlarContext>` when partition selection depends on execution metadata:

```csharp
var tenantKey = new KevlarKey<string>("tenant");
using var limiter = PartitionedRateLimiter.Create<KevlarContext, string>(context =>
    RateLimitPartition.Get(
        context.Properties.GetOrDefault(tenantKey, "default"),
        static _ => new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 10,
            QueueLimit = 20,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        })));
var shield = Shield.Empty.RateLimit(limiter);

await shield.ExecuteWithContextAsync(
    "tenant-42",
    (tenant, properties) => properties.Set(tenantKey, tenant),
    static (_, context) => new ValueTask(Task.Delay(1, context.CancellationToken)));
```

The partition callback receives the live pooled `KevlarContext`; read it only during the callback
and never retain it. One `PartitionedRateLimiter<KevlarContext>` instance shares partition state
across every shield using it, including shields returned by Kevlar's `PartitionedShield<TKey>`.
Partition retention follows the limiter implementation; keep attacker-controlled key cardinality
bounded. The caller owns and disposes the partitioned limiter and its child limiters; Kevlar owns
only each returned lease.

The delegate must return a fresh acquired or rejected lease for each call. Rejection metrics and
hooks follow the built-in contract: metric first, then `OnRejected`, then awaited
`OnRejectedAsync`; a hook failure replaces `RateLimitExceededException`. Cancellation while
queued is cancellation, not rejection, so hooks do not run.

## Options

| Option | Default | What it does |
|---|---|---|
| `Permits` | `100` | Executions allowed per window |
| `Window` | `1s` | The replenishment window |
| `Burst` | = `Permits` | Bucket capacity: how far above the steady rate a burst may spike |
| `QueueLimit` | `0` | How many executions may *wait* for a permit instead of being rejected immediately |
| `OnRejected` | — | Synchronous notification for an actual rejection |
| `OnRejectedAsync` | — | Awaited notification after `OnRejected` |

## Rejection vs queueing

With `QueueLimit = 0`, an execution that finds the bucket empty fails immediately with `RateLimitExceededException`. The exception carries `RetryAfter` — an estimate of when a permit will next be available — which pairs naturally with an outer retry's `DelayGenerator`.

With `QueueLimit > 0`, up to that many executions reserve a future permit and **wait** for it instead of failing. Beyond the queue limit, rejections resume.

For an actual rejection, Kevlar records rejection metrics, invokes `OnRejected`, awaits
`OnRejectedAsync`, then surfaces `RateLimitExceededException`. The event includes `RetryAfter`,
the configured permit/window/burst/queue values, the strategy index, and `KevlarContext`.
A synchronous callback failure skips the asynchronous callback; either callback's failure replaces
the limiter exception and preserves its exception instance. Queued cancellation is cancellation,
not rejection, so it invokes neither hook.

Callback contexts are pooled. Do not retain `RateLimitRejectedEvent.Context` after the synchronous
callback or returned `ValueTask` completes. Hooks run outside limiter locks and may run concurrently
or re-enter the same shield; captured state must be thread-safe.

:::note Queueing is reservation-based, not FIFO
Queued executions each sleep until their reserved permit replenishes; there's no fairness ordering among waiters.
:::

## Placement and sharing

Rate limiting is proactive — it doesn't consult [handling clauses](../handling-failures.md); it acts on every execution that reaches it.

The bucket lives with the shield instance. Reuse one instance for everything hitting the limited dependency, or you'll have several independent buckets each allowing the full rate ([state-sharing rule](../composition.md#the-state-sharing-rule)).

```csharp
// Retry politely around the limiter: waits what the limiter suggests
var polite = Shield
    .When<RateLimitExceededException>()
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
