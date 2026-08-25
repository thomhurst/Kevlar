# gRPC client

This sample wraps a `CallInvoker` with `ShieldUnaryClientInterceptor` and proves that transient `Unavailable` results are retried before returning `pong`. Run `dotnet run --project samples/GrpcClient -f net10.0`; generated clients use the same interceptor registration shown in the gRPC guide.
