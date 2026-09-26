---
sidebar_position: 4
---

# Rate Limit

A token-bucket limiter: `Permits` executions per `Window`, with bursts and optional queueing.

See the [exceptions reference](../exceptions.md) for `RateLimitExceededException` and `RetryAfter`.

```csharp
var fixedRate = Shield.RateLimit(100, perWindow: TimeSpan.FromSeconds(1)); // 100/s, burst = 100

var configuredRate = Shield.RateLimit(o =>
{
    o.Permits = 100;                       // default 100
    o.Window = TimeSpan.FromSeconds(1);    // default 1s
    o.Burst = 200;                         // default: same as Permits
    o.QueueLimit = 20;                     // default 0
    o.OnRejected = rejection =>
    {
        logger.LogWarning("Rate limited; retry after {RetryAfter}", rejection.RetryAfter);
        return default;
    };
});
```

## System.Threading.RateLimiting adapters

Install `Kevlar.Extensions.RateLimiting` to reuse a framework limiter without adding that dependency
to Kevlar core:

```shell
dotnet add package Kevlar.Extensions.RateLimiting
```

<!-- doc-test-declaration -->
```csharp
using Kevlar.Extensions.RateLimiting;
using System.Threading.RateLimiting;

public sealed class RateLimitedDependency : IDisposable
{
    private readonly FixedWindowRateLimiter _limiter = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = 100,
        Window = TimeSpan.FromSeconds(1),
        QueueLimit = 20,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });
    private readonly Shield _shield;

    public RateLimitedDependency()
    {
        _shield = Shield.Empty.UseRateLimiter(_limiter, options =>
        {
            options.PermitCount = 1;
            options.OnRejected = rejection =>
            {
                Console.WriteLine(rejection.RetryAfter);
                return default;
            };
        });
    }

    public ValueTask ExecuteAsync(CancellationToken cancellationToken = default) =>
        _shield.ExecuteAsync(static _ => ValueTask.CompletedTask, cancellationToken);

    public void Dispose() => _limiter.Dispose();
}
```

The caller owns the `RateLimiter` by default. Pass `ownsLimiter: true` when a shield registered in
`IKevlarRegistry` should transfer limiter ownership to the registry; registry disposal then
disposes the limiter exactly once. Every returned `RateLimitLease`
is held until the protected execution completes and is then disposed exactly once. Rejected lease
metadata is copied before disposal. `MetadataName.RetryAfter` becomes
`RateLimiterAdapterRejectedException.RetryAfter`, and the complete immutable snapshot is available from
`RateLimiterAdapterRejectedEvent.Metadata`.

Fixed-window, sliding-window, concurrency, chained, and custom limiters all use the same adapter.
For a limiter owned behind another abstraction, supply asynchronous acquisition directly:

```csharp
using Kevlar.Extensions.RateLimiting;

var shield = Shield.Empty.UseRateLimiter(
    (permitCount, context) =>
        AcquireTenantLeaseAsync(permitCount, context.CancellationToken));
```

Use `PartitionedRateLimiter<KevlarContext>` when partition selection depends on execution metadata:

<!-- doc-test-declaration -->
```csharp
using Kevlar.Extensions.RateLimiting;

public sealed class TenantLimitedDependency : IDisposable
{
    private static readonly KevlarKey<string> _tenantKey = new("tenant");
    private readonly PartitionedRateLimiter<KevlarContext> _limiter;
    private readonly Shield _shield;

    public TenantLimitedDependency()
    {
        _limiter = PartitionedRateLimiter.Create<KevlarContext, string>(context =>
            RateLimitPartition.Get(
                context.Properties.GetOrDefault(_tenantKey, "default"),
                static _ => new ConcurrencyLimiter(new ConcurrencyLimiterOptions
                {
                    PermitLimit = 10,
                    QueueLimit = 20,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                })));
        _shield = Shield.Empty.UseRateLimiter(_limiter);
    }

    public ValueTask ExecuteAsync(string tenant, CancellationToken cancellationToken = default) =>
        _shield.ExecuteWithContextAsync(
            tenant,
            static (value, properties) => properties.Set(_tenantKey, value),
            static (_, context) => new ValueTask(Task.Delay(1, context.CancellationToken)),
            cancellationToken);

    public void Dispose() => _limiter.Dispose();
}
```

The partition callback receives the live pooled `KevlarContext`; read it only during the callback
and never retain it. One `PartitionedRateLimiter<KevlarContext>` instance shares partition state
across every shield using it, including shields returned by Kevlar's
[`PartitionedShield<TKey>`](../partitioning.md).
Partition retention follows the limiter implementation; keep attacker-controlled key cardinality
bounded. The caller owns and disposes the partitioned limiter and its child limiters by default.
The same `ownsLimiter: true` opt-in transfers ownership when registry disposal governs the shield
lifetime. A Kevlar [`PartitionedShield<TKey>`](../partitioning.md) also disposes factory-returned
strategies by default; set its `OwnsStrategies` option to `false` when the same strategy is used
elsewhere. Kevlar always owns each returned lease.

The delegate must return a fresh acquired or rejected lease for each call. Rejection metrics and
hooks follow the built-in contract: metric first, then awaited `OnRejected`. Hook failures are
reported through `KevlarDiagnostics.OnCallbackError`, and
`RateLimiterAdapterRejectedException` remains the outcome. Cancellation while queued is cancellation,
not rejection, so hooks do not run.

## Options

API reference: [`RateLimitOptions`](pathname:///api/Kevlar.RateLimitOptions.html).

| Option | Default | What it does |
|---|---|---|
| `Permits` | `100` | Executions allowed per window |
| `Window` | `1s` | The replenishment window |
| `Burst` | = `Permits` | Bucket capacity: how far above the steady rate a burst may spike |
| `QueueLimit` | `0` | How many executions may *wait* for a permit instead of being rejected immediately |
| `UsePriorityQueue` | `false` | Highest priority first, FIFO ties, and lower-priority eviction |
| `QueueTimeout` | `null` | Maximum queue residence time; execution time is excluded |
| `OnRejected` | — | Awaited notification for an actual rejection; return `default` when the work is synchronous |

Invalid option values throw [`KevlarConfigurationException`](../exceptions.md#configuration-failures)
and identify the options type, property, and offending value.

## Rejection vs queueing

With `QueueLimit = 0`, an execution that finds the bucket empty fails immediately with `RateLimitExceededException`. The exception carries `RetryAfter` — an estimate of when a permit will next be available — which pairs naturally with an outer retry's `DelayGenerator`.

With `QueueLimit > 0`, up to that many executions reserve a future permit and **wait** for it instead of failing. Beyond the queue limit, rejections resume.

For an actual rejection, Kevlar records rejection metrics, awaits `OnRejected`, then surfaces
`RateLimitExceededException`. The event includes `RetryAfter`, the configured
permit/window/burst/queue values, the strategy index, and `KevlarContext`. Under the shared
[callback-failure contract](../observability.md#callback-failures), failures are reported through
`KevlarDiagnostics.OnCallbackError`, and `RateLimitExceededException` remains the rejection outcome.
Queued cancellation is cancellation, not rejection, so it does not invoke the hook. A hook that
completes synchronously works with synchronous `Execute`; one that yields throws
`NotSupportedException` there and must run through `ExecuteAsync`.

Callback contexts are pooled. Do not retain `RateLimitRejectedEvent.Context` after the returned
`ValueTask` completes. Hooks run outside limiter locks and may run concurrently or re-enter the
same shield; captured state must be thread-safe.

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
        o.DelayGenerator = e => new((e.Exception as RateLimitExceededException)?.RetryAfter);
    })
    .RateLimit(100, perWindow: TimeSpan.FromSeconds(1));
```

:::warning Sync callers block
In synchronous `Execute`, queued waits block the calling thread. Prefer `ExecuteAsync` for queue-enabled limiters.
:::

## Priority queues

Set `UsePriorityQueue = true` to admit queued work by `KevlarKeys.Priority`:

```csharp
var shield = Shield.RateLimit(options =>
{
    options.Permits = 100;
    options.Window = TimeSpan.FromSeconds(1);
    options.QueueLimit = 20;
    options.QueueTimeout = TimeSpan.FromMilliseconds(250);
    options.UsePriorityQueue = true;
});

var result = await shield.ExecuteWithContextAsync(
    10,
    static (priority, properties) => properties.Set(KevlarKeys.Priority, priority),
    static (_, context) => new ValueTask<int>(42));
```

Higher integers run first; negative values are valid. An absent priority means zero.
Equal priorities retain FIFO arrival order. When the queue is full, a strictly higher
priority arrival evicts the newest waiter at the lowest priority. Equal or lower arrivals
are rejected immediately. Running work is never evicted. Continuous high-priority traffic
can starve lower priorities, so combine priority queueing with `QueueTimeout` when needed.

Eviction returns `RateLimitExceededException` to the evicted caller. Its `OnRejected.Reason`
and telemetry `RejectionReason` are `queue_evicted`; the rejection counter includes
`kevlar.rejection.reason=queue_evicted`. Cancellation remains cancellation. Queue expiry
keeps the `queue_timeout` reason and stops affecting the call after admission.

`shield.ToString()` includes `priority queue`, and its testing descriptor exposes
`UsePriorityQueue`. With `Kevlar.Testing`, `GetStateSnapshot()` includes an immutable
`QueuedByPriority` dictionary on the limiter snapshot. An unset priority appears under
zero. Priority values are not metric tags. Ordinary queues return an empty dictionary.

The option defaults to `false`; setting a priority alone does not change admission.
`QueueLimit = 0` still rejects immediately. The same option is available in typed shields,
`ShieldDefinition`, and configuration binding. Uncontended admission remains allocation-free.

Priority queues acquire tokens only at admission; waiting requests do not reserve future
tokens. Reordering, cancellation, and eviction therefore cannot leave token debt behind.

## Queue timeout

Bound reservation waiting without limiting admitted execution time:

```csharp
var shield = Shield.RateLimit(options =>
{
    options.Permits = 100;
    options.Window = TimeSpan.FromSeconds(1);
    options.QueueLimit = 20;
    options.QueueTimeout = TimeSpan.FromMilliseconds(250);
});
```

The queue timer uses the shield's `TimeProvider`. Expiry removes and refunds the
reservation, then reports `RateLimitExceededException` with no `RetryAfter` estimate.
`OnRejected.Reason` and telemetry `RejectionReason` are `queue_timeout`; the rejection
counter includes `kevlar.rejection.reason=queue_timeout`. Caller cancellation remains
cancellation and wins when already requested as the waiter observes expiry.

The default `null` retains unbounded queue waiting. Configured values must be positive
and at most 4,294,967,294 milliseconds. No timer is created for immediate admission.
Once a permit is granted the queue timer stops, so it cannot cancel protected work.
`shield.ToString()` includes `queue 20/250ms`. Synchronous `Execute` blocks during the wait
and observes the same queue timeout.
