---
sidebar_position: 10
---

# gRPC Integration

`Kevlar.Extensions.Grpc` supplies separate interceptors for asynchronous unary and streaming gRPC client calls. Blocking unary and server calls remain unchanged.

```bash
dotnet add package Kevlar.Extensions.Grpc
```

## Choose transient failures explicitly

gRPC status handling is opt-in. `GrpcShield.WhenTransient()` handles `RpcException` only for:

- `Unavailable`
- `DeadlineExceeded`
- `ResourceExhausted`

It does not retry `Cancelled`, validation failures, authentication failures, or other statuses.

<!-- doc-test-ignore: requires an application-generated gRPC client and channel -->
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

## Streaming calls

Use `ShieldStreamingClientInterceptor` for server-streaming, client-streaming, and duplex calls:

<!-- doc-test-ignore: requires an application-generated gRPC client and channel -->
```csharp
var streamingShield = Shield.Timeout(TimeSpan.FromSeconds(5));
var client = new Orders.OrdersClient(
    channel.Intercept(new ShieldStreamingClientInterceptor(streamingShield)));
```

The interceptor uses explicit progress boundaries:

- Server streaming may retry or hedge establishment only until response headers or the first item becomes observable. After that point, it never repeats `MoveNext`, so an item cannot be skipped or duplicated. With an at-most-once shield, each later `MoveNext` remains protected by that shield.
- Client streaming and duplex never buffer or replay request messages. Their shield must be at-most-once; constructing either call with retry, hedging, or another repeating strategy throws `NotSupportedException` before the RPC starts.
- Request writes, duplex reads, and duplex writes run through the operation shield. Client-streaming response completion follows the total call lifetime, so waiting for the response cannot occupy an operation concurrency slot. Disposing the wrapper cancels and disposes the underlying call. Status and trailers remain available after normal completion until disposal.

When server-stream establishment needs retry or hedging and later reads still need a timeout, supply separate shields:

<!-- doc-test-ignore: requires an application-generated gRPC client and channel -->
```csharp
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

<!-- doc-test-ignore: requires an application-generated gRPC client -->
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

The interceptor uses no runtime code generation or reflection and targets `netstandard2.0`, `netstandard2.1`, and `net10.0`. The `netstandard2.1` asset preserves per-write cancellation when modern applications resolve gRPC's cancellable streaming interface. It is trimming- and NativeAOT-compatible when the generated gRPC client and its serializer are compatible. Generated protobuf clients are the supported baseline. Validate the complete application's transport, TLS, serializer, and DI configuration with your publish target; the repository package smoke tests cover trimmed, single-file, and NativeAOT consumers where the platform supports them.
