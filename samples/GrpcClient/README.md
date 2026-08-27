# gRPC client

This sample wraps a `CallInvoker` with `ShieldUnaryClientInterceptor` and proves that transient `Unavailable` results are retried before returning `pong`. Run `dotnet run --project samples/GrpcClient -f net10.0`; generated clients use the same interceptor registration shown in the gRPC guide.

The transport is an in-process stub, so no server, TLS certificate, or generated protobuf client is
required. Focus on the interceptor boundary: each retry creates a new unary call while preserving
the request and call options. In a real application, apply multiple attempts only to idempotent
operations and keep the gRPC deadline large enough for the complete retry budget.
