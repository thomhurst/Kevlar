---
sidebar_position: 22
---

# Thread-safety guarantees

Kevlar is designed for shared resilience pipelines. A shield is normally built once and used by
many concurrent callers. The table below distinguishes objects safe to share from configuration and
execution-scoped objects that must remain owned by one operation.

| Types | Guarantee |
|---|---|
| `Shield`, `Shield<TResult>`, `ShieldBuilder`, `ShieldBuilder<TResult>` | Immutable and thread-safe. Fluent calls return new values; copies intentionally share existing stateful strategies. |
| [`PartitionedShield<TKey>` and `PartitionedShield<TKey, TResult>`](partitioning.md) | Thread-safe. Partition creation is coordinated and retained state is bounded by `PartitionedShieldOptions`. Eviction can occur immediately after a lookup, so a snapshot is never a reservation. |
| `CircuitBreakerMonitor` | Thread-safe and bindable to multiple circuit breakers. `StateChanged` notifications are serialized per breaker; different breakers may publish concurrently. |
| `KevlarContext`, `KevlarProperties` | Execution-scoped and not safe for caller-created concurrent access. Do not retain them after the delegate or callback returns. Hedge attempts receive detached property containers, but mutable values stored inside them remain the caller's responsibility. |
| `IKevlarRegistry`, `IShieldProvider` | Thread-safe singleton services. Registry lookups return immutable snapshots. Reloading names expose only a keyed provider; query it or the registry once per operation. |
| `ExecutionProbe`, `TelemetryRecorder` | Thread-safe test observers. Their snapshot collections are immutable copies; dispose `TelemetryRecorder` to detach its listener. |
| Custom `Strategy` implementations | One instance may be used by every concurrent execution and composed shield. Implementations must synchronize mutable state and must not retain a pooled `KevlarContext`. |

## Configuration objects

Options and configuration definitions are mutable setup objects. Do not mutate or configure one
instance concurrently. Fluent factories read and validate them while building a strategy; later
changes do not reconfigure an already-built shield.

HTTP handler options are the exception: `ShieldDelegatingHandler(shield, options)` and the
`IHttpClientBuilder.AddShield(...)` options overloads retain the exact `ShieldHttpHandlerOptions`
instance supplied directly or by the options factory. Treat it as immutable after handoff;
mutating it can change live replay or routing behavior, and racing mutations are unsafe.

The following public mutable types follow that rule:

| Package | Mutable setup or control types |
|---|---|
| Core | `CircuitBreakerOptions`, `CircuitBreakerOptions<TResult>`, `ConcurrencyLimitOptions`, `FallbackOptions`, `FallbackOptions<TResult>`, `HedgeOptions`, `HedgeOptions<TResult>`, `PartitionedShieldOptions`, `RateLimitOptions`, `RetryOptions`, `RetryOptions<TResult>`, `TimeoutOptions` |
| Chaos | `ChaosBehaviorOptions`, `ChaosFaultOptions`, `ChaosLatencyOptions`, `ChaosOutcomeOptions<TResult>` |
| Dependency injection | `CircuitBreakerDefinition`, `ConcurrencyLimitDefinition`, `RateLimitDefinition`, `ReloadingShieldOptions`, `RetryDefinition`, `ShieldDefinition` |
| HTTP | `HttpEndpointRoutingOptions`, `KevlarRequestOptions`, `ShieldHttpHandlerOptions`, `StandardHedgeShieldOptions`, `StandardHttpShieldOptions` |
| Rate-limiter adapter | `RateLimiterAdapterOptions` |

`CircuitBreakerMonitor`, `KevlarContext`, `KevlarProperties`, `ExecutionProbe`, and
`TelemetryRecorder` also contain mutable state, but their ownership rules are described separately
above because they are live controls or observations rather than setup objects.

`KevlarRequestOptions` is request-scoped. Configure it before calling `SendAsync`, then do not
mutate it while that request is executing; built-in replay clones share the same options instance.

## User delegates and values

Thread safety does not make the protected delegate idempotent. Hedging can invoke it concurrently;
retry can invoke it sequentially more than once. Synchronize shared application state and use typed
result handling to select acceptable hedge outcomes.

User callbacks and generators must also tolerate concurrent invocation when a shield is shared.
This includes retry, timeout, fallback, limiter, hedging, and chaos hooks. Do not capture
unsynchronized mutable state unless the specific callback contract guarantees serialization;
circuit-breaker transition notifications are one such serialized contract.

`KevlarProperties` copies entries for hedge attempts, not object graphs. If a property value is a
mutable list, stream, request, or domain object, attempts still see the same reference unless the
application supplies an independent value. Prefer immutable property values or an attempt-specific
factory.
