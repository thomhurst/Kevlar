using Grpc.Core;
using Grpc.Core.Interceptors;
using Kevlar;
using Kevlar.Extensions.Grpc;

var transport = new FlakyCallInvoker();
var shield = GrpcShield.WhenTransient()
    .Retry(2, Backoff.None)
    .WithName("grpc-client");
var client = transport.Intercept(new ShieldUnaryClientInterceptor(shield));
var method = new Method<PingRequest, PingReply>(
    MethodType.Unary,
    "samples.Ping",
    "Get",
    Marshallers.Create(static _ => [], static _ => new PingRequest()),
    Marshallers.Create(static reply => System.Text.Encoding.UTF8.GetBytes(reply.Message),
        static bytes => new PingReply(System.Text.Encoding.UTF8.GetString(bytes))));

using var call = client.AsyncUnaryCall(method, null, new CallOptions(), new PingRequest());
var reply = await call.ResponseAsync;

if (reply.Message != "pong" || transport.Attempts != 3)
{
    throw new InvalidOperationException(
        $"Expected the gRPC client to recover on attempt 3; reply={reply.Message}, attempts={transport.Attempts}.");
}

Console.WriteLine("gRPC client sample passed after two transient Unavailable responses.");

internal sealed class PingRequest;
internal sealed record PingReply(string Message);

internal sealed class FlakyCallInvoker : CallInvoker
{
    private int _attempts;

    public int Attempts => Volatile.Read(ref _attempts);

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request)
    {
        var attempt = Interlocked.Increment(ref _attempts);
        var response = attempt < 3
            ? Task.FromException<TResponse>(new RpcException(new Status(StatusCode.Unavailable, "transient")))
            : Task.FromResult((TResponse)(object)new PingReply("pong"));
        return new AsyncUnaryCall<TResponse>(
            response,
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => new Metadata(),
            static () => { });
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) => throw new NotSupportedException();

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) => throw new NotSupportedException();

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options) => throw new NotSupportedException();

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options) => throw new NotSupportedException();
}
