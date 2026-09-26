# Kevlar.Extensions.Grpc

Protect asynchronous unary and streaming gRPC client calls with Kevlar interceptors, transient
status helpers, cancellation safety, and named-shield registration. `ShieldServerInterceptor`
adds shared or partitioned admission control around all four server handler shapes, with gRPC
status mapping for concurrency, rate-limit, and circuit-breaker rejections.

```shell
dotnet add package Kevlar.Extensions.Grpc
```

```csharp
using Kevlar.Extensions.Grpc;

var shield = GrpcShield.WhenTransient()
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
```

See the [gRPC integration guide](https://thomhurst.github.io/Kevlar/docs/grpc) for interceptor
registration, streaming progress boundaries, deadlines, and replay safety.
