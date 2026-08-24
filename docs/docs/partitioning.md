---
sidebar_position: 5
---

# Partitioned shields

A stateful shield normally shares one circuit, rate bucket, or concurrency limit wherever that
shield instance is reused. `PartitionedShield<TKey>` keeps an independent shield per endpoint,
tenant, shard, authority, or operation while placing an explicit bound on retained state.

```csharp
var endpoints = new PartitionedShield<string>(
    endpoint => Shield.When<TimeoutException>()
        .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30)),
    new PartitionedShieldOptions
    {
        MaximumPartitions = 500,
        IdleExpiration = TimeSpan.FromMinutes(20),
    },
    StringComparer.OrdinalIgnoreCase);

var shield = endpoints.GetShield("inventory.internal");
```

Calls with the same key receive the same immutable shield and share its strategy state. Different
keys receive different shields. Concurrent first lookup runs the partition factory exactly once.
The typed `PartitionedShield<TKey, TResult>` variant returns `Shield<TResult>` and supports
result-aware handling and fallbacks.

## Retention and eviction

Every provider is bounded. `MaximumPartitions` defaults to 1,000, and the least recently used
partition is evicted before that limit can be exceeded. `IdleExpiration` is optional and
opportunistic: expired entries are removed by later provider operations or an explicit
`PruneExpired()` call; no timer or background worker is retained.

Eviction removes only the provider's reference to a shield. An execution already using that shield
continues normally and is not cancelled. A later lookup of the evicted key creates a new shield
with fresh breaker, limiter, and queue state. This makes capacity eviction deterministic without
coupling cache lifetime to execution lifetime.

`Count`, `CreatedCount`, `CapacityEvictionCount`, and `ExpirationEvictionCount` expose lifecycle
status without retaining evicted keys or shields. `TryGetShield`, `Remove`, and `Clear` provide
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
        options.MaximumPartitions = 200;
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
        .When<TimeoutException>()
        .CircuitBreaker(consecutiveFailures: 3, breakDuration: TimeSpan.FromSeconds(20))
        .WithName("tenant-operation"),
    options => options.MaximumPartitions = 5_000,
    StringComparer.Ordinal);
```

## Metrics cardinality

Partition keys are not copied into `Shield.Name`, `KevlarContext`, or metric tags. Give partitions
one shared low-cardinality name when aggregate telemetry is useful. If a controlled key dimension
is required, add it in application-owned instrumentation after applying your own cardinality bound;
do not use unrestricted tenant IDs or URLs as metric labels.

Partitioning is outside the core execution path. Non-partitioned shields are unchanged, and a warm
`GetShield` lookup uses the existing dictionary entry without per-call closure allocation.
