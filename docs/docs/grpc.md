---
sidebar_position: 10
---

# gRPC Integration

`Kevlar.Extensions.Grpc` supplies interceptors for asynchronous unary and streaming gRPC client
calls, plus server admission control. Client interceptors leave blocking unary and server calls unchanged.

```bash
dotnet add package Kevlar.Extensions.Grpc
```

## Server admission control

Use `ShieldServerInterceptor` to hold a concurrency permit for the entire handler lifetime,
including all reads and writes in a stream. Register both the interceptor instance and its place
in the ASP.NET Core gRPC pipeline:

```csharp
using Kevlar;
using Kevlar.Extensions.Grpc;
using Microsoft.Extensions.DependencyInjection;

services.AddShieldServerInterceptor(Shield.ConcurrencyLimit(100, queueLimit: 20));
services.AddGrpc(options => options.Interceptors.Add<ShieldServerInterceptor>());
```

Alternatively, register a named shield with `services.AddShield("server", shield)` and use
`services.AddShieldServerInterceptor("server")`. The interceptor is a singleton and shares the
resolved shield's state across unary, client-streaming, server-streaming, and duplex calls.

For independent limits per full gRPC method name, supply a bounded partition provider:

```csharp
using Kevlar;
using Microsoft.Extensions.DependencyInjection;

var serverPartitions = new PartitionedShield<string>(
    _ => Shield.ConcurrencyLimit(10, queueLimit: 0));
services.AddShieldServerInterceptor(serverPartitions);
```

Pass `partitionKey: context => context.Peer` to partition by peer instead. Peer includes transport
identity details and may change between connections; choose a stable application key when needed.
The caller owns the provider and must dispose it after the server stops. Asynchronous partition
factories are supported. Keep partition cardinality bounded through `PartitionedShieldOptions`.

- `ConcurrencyLimitExceededException` and `RateLimitExceededException` become `ResourceExhausted`.
- `CircuitOpenException` becomes `Unavailable`. A known remaining break duration becomes a
  `grpc-retry-pushback-ms` trailer, rounded up to milliseconds and capped at `Int32.MaxValue`.
  Isolated circuits have no automatic recovery estimate and omit that trailer.
- Existing `RpcException` statuses and trailers pass through unchanged. Other exceptions retain
  the host's normal gRPC exception handling.

Server shields must guarantee at most one handler invocation. Retry, hedging, live-forwarding
shields, and custom strategies without that guarantee are rejected; a server cannot rewind request
streams or retract sent response messages. Use client-side retry for replay-safe RPCs. Zero-retry
and zero-hedge strategies are supported.

Caller cancellation reaches the shield and handler. The original `ServerCallContext` is preserved,
including request metadata, deadlines, HTTP context, response headers, status, trailers, and native
context propagation. Cancellation remains cooperative: pass its token to downstream operations.
Use gRPC deadlines for time limits. Shield timeouts and other strategies that replace the transport
token are rejected before handler execution: substituting a context wrapper would break ASP.NET
Core service activation. This admission integration adds no ASP.NET Core dependency to client apps
and remains available on every target framework supported by `Kevlar.Extensions.Grpc`.

## Choose transient failures explicitly

gRPC status handling is opt-in. `GrpcShield.WhenTransient()` handles `RpcException` only for:

- `Unavailable`
- `DeadlineExceeded`
- `ResourceExhausted`

It does not retry `Cancelled`, validation failures, authentication failures, or other statuses.

```csharp
using Grpc.Core.Interceptors;
using Kevlar;
using Kevlar.Extensions.Grpc;

var shield = GrpcShield.WhenTransient()
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));

var client = new Orders.OrdersClient(
    channel.Intercept(new ShieldUnaryClientInterceptor(shield)));
```

The final response or `RpcException` remains the caller's result. Superseded retry calls and losing hedge calls are disposed. Response headers, status, and trailers come from the selected final attempt.

## Choose one retry owner

`Grpc.Net.Client` can apply a `ServiceConfig` `RetryPolicy` or `HedgingPolicy` transparently inside
the channel. Those policies can target individual methods, stop after response headers or request
buffer limits commit a call, honour server pushback, and share retry throttling for a server name.
Kevlar operates outside the channel: its interceptor can apply retry, circuit breaker, hedging,
fallback, timeout, and observability consistently across methods.

Do not configure both a ServiceConfig retry policy and a Kevlar retry for the same call. Their
attempt counts multiply, and the outer Kevlar shield sees only the channel's final result. Choose
ServiceConfig when protocol-native, per-method retry with built-in throttling is sufficient. Choose
Kevlar when retry must compose with its other strategies or share their telemetry and state.
Kevlar can also share a [retry budget](strategies/retry.md#shared-retry-budgets) across shields and
partitions. This provides success/failure feedback throttling; it does not add the channel's
protocol-level call commitment or buffering rules.

When Kevlar owns retry, opt in to the server's `grpc-retry-pushback-ms` trailer:

```csharp
using Kevlar;
using Kevlar.Extensions.Grpc;

var retryingShield = GrpcShield.WhenTransient()
    .Retry(options =>
    {
        options.MaxRetries = 3;
        options.DelayGenerator = GrpcShield.RetryAfter;
    });
```

A valid non-negative trailer replaces the computed delay for that retry. A negative or malformed
value suppresses further retries and hedges across the current execution, including nested child
shields. When an inner-aware handling clause accepts a wrapped `RpcException`, pushback is read
from that same ordinary or aggregate exception graph. `RetryOptions.MaxDelay` still caps server delays; when it is
unset, the selected backoff's maximum applies (30 seconds for `Backoff.Default`). Assigning the
method directly also inspects a handled final outcome when no retry budget remains. A wrapper that
forwards to `GrpcShield.RetryAfter` registers that terminal inspection on its first invocation.

## Streaming calls

Use `ShieldStreamingClientInterceptor` for server-streaming, client-streaming, and duplex calls:

```csharp
using Grpc.Core.Interceptors;
using Kevlar.Extensions.Grpc;

var streamingShield = Shield.Timeout(TimeSpan.FromSeconds(5));
var client = new Orders.OrdersClient(
    channel.Intercept(new ShieldStreamingClientInterceptor(streamingShield)));
```

The interceptor uses explicit progress boundaries:

- Server streaming may retry or hedge establishment only until response headers or the first item becomes observable. After that point, it never repeats `MoveNext`, so an item cannot be skipped or duplicated. With an at-most-once shield, each later `MoveNext` remains protected by that shield.
- Client streaming and duplex never buffer or replay request messages. Their shield must be at-most-once; constructing either call with retry, hedging, or another repeating strategy throws `NotSupportedException` before the RPC starts.
- Request writes, duplex reads, and duplex writes run through the operation shield. Client-streaming response completion follows the total call lifetime, so waiting for the response cannot occupy an operation concurrency slot. Disposing the wrapper cancels and disposes the underlying call. Status and trailers remain available after normal completion until disposal.

When server-stream establishment needs retry or hedging and later reads still need a timeout, supply separate shields:

```csharp
using Grpc.Core.Interceptors;
using Kevlar.Extensions.Grpc;

var establishment = GrpcShield.WhenTransient().Retry(2, Backoff.None);
var operations = Shield.Timeout(TimeSpan.FromSeconds(3));
var interceptor = new ShieldStreamingClientInterceptor(establishment, operations);
var client = new Orders.OrdersClient(channel.Intercept(interceptor));
```

The operation shield must be at-most-once. The two-shield constructor rejects retry, hedging, or any custom strategy that may repeat its continuation. With the one-shield constructor, a repeating shield protects only pre-progress server establishment; later reads run directly, and client/duplex calls reject that shield.

| Boundary | Owner |
|---|---|
| Server-stream establishment through headers or first item | establishment shield |
| Individual request writes and post-progress response reads | at-most-once operation shield |
| Client-streaming response completion | gRPC deadline, caller cancellation token, or call disposal |
| Total lifetime, including time when no read/write is active | gRPC deadline, caller cancellation token, or call disposal |

A Kevlar operation timeout is not an idle-stream timer. Use the gRPC deadline when the entire stream needs one absolute budget; its timestamp is preserved across server-establishment attempts.

`WriteAsync(message, cancellationToken)` uses the operation token on the `netstandard2.1` and `net10.0` targets. On the `netstandard2.0` compatibility target, where gRPC exposes only `WriteAsync(message)`, operation cancellation cancels the call lifetime so a blocked write can complete. A gRPC deadline remains in the original `CallOptions` for every server-streaming establishment attempt.

## Dependency injection and named shields

The package integrates with `Grpc.Net.ClientFactory` and the existing Kevlar registry:

```csharp
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Grpc;
using Microsoft.Extensions.DependencyInjection;

services.AddShield(
    "orders-grpc",
    GrpcShield.WhenTransient()
        .Retry(3)
        .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30)));

services.AddGrpcClient<Orders.OrdersClient>(options =>
        options.Address = new Uri("https://orders.example"))
    .AddShieldUnaryInterceptor("orders-grpc")
    .AddShieldStreamingInterceptor("orders-grpc");
```

Both registration methods accept a `Shield` instance, an `IServiceProvider` factory, or a named shield. Reuse one shield when calls should share circuit-breaker or limiter state. Register both interceptors when a generated client exposes unary and streaming methods; each interceptor handles only its own call shapes.

## Cancellation, deadlines, and timeouts

Caller cancellation and cancellation created by Kevlar strategies are passed to every underlying RPC attempt. Disposing the returned `AsyncUnaryCall<T>` cancels active attempts.

A gRPC deadline is an absolute timestamp and is preserved unchanged across retries and hedges. A Kevlar timeout is relative to its position in the shield:

```csharp
using Kevlar.Extensions.Grpc;

var shield = Shield.Timeout(TimeSpan.FromSeconds(10)) // total Kevlar budget
    .When<Grpc.Core.RpcException>(GrpcShield.IsTransient)
    .Retry(2)
    .Timeout(TimeSpan.FromSeconds(3));                // per-attempt budget
```

Whichever expires first wins. An expired gRPC deadline remains an `RpcException` with `DeadlineExceeded`; an expired Kevlar timeout surfaces `TimeoutExceededException`. Set the gRPC deadline long enough for the intended retry budget, or omit it when Kevlar owns the complete budget.

## Retry and hedging safety

Every retry or hedge starts a new RPC with the same request object and call options. Use multiple attempts only for idempotent methods, or when the server provides an idempotency key/deduplication contract. Do not retry or hedge a mutation merely because its transport result is unknown.

For server streaming, retry and hedge only operations that are safe to repeat before progress. Client and duplex streaming reject repeating strategies because Kevlar deliberately provides no implicit replay buffer. If an application needs replay, implement an explicit bounded message store and a protocol-level idempotency/deduplication contract outside the interceptor.

## Trimming and NativeAOT

The interceptor uses no runtime code generation or reflection and targets `netstandard2.0`, `netstandard2.1`, `net8.0`, and `net10.0`. The `netstandard2.1` asset preserves per-write cancellation when modern applications resolve gRPC's cancellable streaming interface. It is trimming- and NativeAOT-compatible when the generated gRPC client and its serializer are compatible. Generated protobuf clients are the supported baseline. Validate the complete application's transport, TLS, serializer, and DI configuration with your publish target; the repository package smoke tests cover trimmed, single-file, and NativeAOT consumers where the platform supports them.
