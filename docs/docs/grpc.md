---
sidebar_position: 9
---

# gRPC Integration

`Kevlar.Extensions.Grpc` sends asynchronous unary gRPC client calls through a shared shield. It leaves blocking unary, streaming, and server calls unchanged.

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
    .CircuitBreaker(5, TimeSpan.FromSeconds(30));

var client = new Orders.OrdersClient(
    channel.Intercept(new ShieldUnaryClientInterceptor(shield)));
```

The final response or `RpcException` remains the caller's result. Superseded retry calls and losing hedge calls are disposed. Response headers, status, and trailers come from the selected final attempt.

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
        .CircuitBreaker(5, TimeSpan.FromSeconds(30)));

services.AddGrpcClient<Orders.OrdersClient>(options =>
        options.Address = new Uri("https://orders.example"))
    .AddShieldUnaryInterceptor("orders-grpc");
```

`AddShieldUnaryInterceptor` also accepts a `Shield` instance or an `IServiceProvider` factory. Reuse one shield when calls should share circuit-breaker or limiter state.

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

The initial package intentionally excludes streaming. Stream lifetime, partial messages, and ownership require a different API and cleanup contract; calls pass through the interceptor unchanged.

## Trimming and NativeAOT

The interceptor uses no runtime code generation or reflection and targets `netstandard2.0` and `net10.0`. It is trimming- and NativeAOT-compatible when the generated gRPC client and its serializer are compatible. Generated protobuf clients are the supported baseline. Validate the complete application's transport, TLS, serializer, and DI configuration with your publish target; the repository package smoke tests cover trimmed, single-file, and NativeAOT consumers where the platform supports them.
