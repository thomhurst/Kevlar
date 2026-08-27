---
sidebar_position: 5
---

# Partitioned shields

A stateful shield normally shares one circuit, rate bucket, or concurrency limit wherever that
shield instance is reused. `PartitionedShield<TKey>` keeps an independent shield per endpoint,
tenant, shard, authority, or operation while placing an explicit bound on retained state.

```csharp
var endpoints = new PartitionedShield<string>(
    endpoint => Shield.When<TimeoutExceededException>()
        .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30)),
    new PartitionedShieldOptions<string>
    {
        MaxPartitions = 500,
        IdleExpiration = TimeSpan.FromMinutes(20),
    },
    StringComparer.OrdinalIgnoreCase);

var shield = endpoints.GetShield("inventory.internal");
```

Calls with the same key receive the same immutable shield and share its strategy state. Different
keys receive different shields. Concurrent first lookup runs the partition factory exactly once.
For factories that perform asynchronous initialization, use `CreateAsync` and `GetShieldAsync` so
same-key callers await the shared creation instead of blocking thread-pool threads:

```csharp
var asyncPartitions = PartitionedShield<string>.CreateAsync(async key =>
{
    await Task.Yield();
    return Shield.Retry(2, Backoff.Exponential(TimeSpan.FromMilliseconds(20)));
});

var asyncShield = await asyncPartitions.GetShieldAsync("inventory.internal");
```

The typed `PartitionedShield<TKey, TResult>` variant returns `Shield<TResult>` and supports
result-aware handling and fallbacks.

## Retention and eviction

Every provider is bounded. `MaxPartitions` defaults to 1,000, and the least recently used
partition is evicted before that limit can be exceeded. `IdleExpiration` is optional and
opportunistic: expired entries are removed by later provider operations or an explicit
`PruneExpired()` call; no timer or background worker is retained.

Eviction removes only the provider's reference to a shield. An execution already using that shield
continues normally and is not cancelled. A later lookup of the evicted key creates a new shield
with fresh breaker, limiter, and queue state. This makes capacity eviction deterministic without
coupling cache lifetime to execution lifetime.

Lifecycle callbacks report both the key and shield and return `ValueTask`; return `default` for
synchronous work. Callback failures are swallowed so telemetry or cleanup cannot fail a lookup. The
eviction callback is awaited before a capacity slot is reused. If that callback performs a cold lookup while its caller owns all available capacity, the
nested lookup receives an unretained shield instead of waiting on its own reservation; a later
lookup creates and retains the partition normally. Explicit `TryRemove` and `Clear` removals use the
`Cleared` reason; idle expiry uses `Expiration`.

```csharp
var observed = new PartitionedShield<string>(
    static _ => Shield.Empty,
    new PartitionedShieldOptions<string>
    {
        MaxPartitions = 2,
        OnCreated = item =>
        {
            Console.WriteLine($"Created {item.Key}");
            return default;
        },
        OnEvicted = async item =>
        {
            await Task.Yield();
            Console.WriteLine($"Evicted {item.Key}: {item.Reason}");
        },
    });
```

`Count`, `CreatedCount`, `EvictionCount`, and the reason-specific eviction counters expose
lifecycle status. `TryGetShield`, `TryRemove`, `Clear`, and their async cleanup variants provide
explicit cache control. If the factory throws, no existing partition is evicted and the failed key
is not cached.

## Dependency injection

`AddPartitionedShield` registers a named provider as a keyed singleton. The factory receives the
application service provider and the partition key.

<!-- doc-test-ignore: EndpointClient and SendAsync are application-specific. -->
```csharp
services.AddPartitionedShield<Uri>(
    "endpoints",
    (serviceProvider, endpoint) => Shield.When<HttpRequestException>()
        .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30))
        .WithName("outbound-endpoint"),
    options =>
    {
        options.MaxPartitions = 200;
        options.IdleExpiration = TimeSpan.FromMinutes(15);
    });

public sealed class EndpointClient(
    [FromKeyedServices("endpoints")] PartitionedShield<Uri> shields)
{
    public Task<HttpResponseMessage> SendAsync(Uri endpoint, CancellationToken cancellationToken) =>
        shields.GetShield(endpoint)
            .ExecuteAsync(token => SendCoreAsync(endpoint, token), cancellationToken)
            .AsTask();
}
```

Typed providers use two generic arguments. This multi-tenant example isolates fallback and breaker
state per tenant while sharing one bounded provider:

<!-- doc-test-ignore: TenantRequest is application-specific. -->
```csharp
services.AddPartitionedShield<string, TenantResult>(
    "tenants",
    (serviceProvider, tenantId) => Shield.For<TenantResult>()
        .When<TimeoutExceededException>()
        .CircuitBreaker(consecutiveFailures: 3, breakDuration: TimeSpan.FromSeconds(20))
        .WithName("tenant-operation"),
    options => options.MaxPartitions = 5_000,
    StringComparer.Ordinal);
```

## Metrics cardinality

`kevlar.partitions.evictions` counts removals with the bounded `kevlar.partition.reason` tag. The
reason is `capacity`, `idle`, or `cleared`; the partition key is deliberately omitted.

Partition keys are not copied into `Shield.Name`, `KevlarContext`, or metric tags. Give partitions
one shared low-cardinality name when aggregate telemetry is useful. State gauges aggregate matching
name/index series: concurrency and rate values are summed, while
`kevlar.circuit_breaker.instances` reports a count for each circuit state. If a controlled key dimension
is required, add it in application-owned instrumentation after applying your own cardinality bound;
do not use unrestricted tenant IDs or URLs as metric labels.

Partitioning is outside the core execution path. Non-partitioned shields are unchanged, and a warm
`GetShield` lookup uses the existing dictionary entry without per-call closure allocation.
