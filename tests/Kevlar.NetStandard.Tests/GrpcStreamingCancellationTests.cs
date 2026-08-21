using Grpc.Core;
using Grpc.Core.Interceptors;
using Kevlar.Extensions.Grpc;

namespace Kevlar.NetStandard.Tests;

public class GrpcStreamingCancellationTests
{
    [Test]
    public async Task Client_Write_Timeout_Cancels_The_NetStandard_Call_Lifetime()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interceptor = new ShieldStreamingClientInterceptor(
            Shield.Timeout(TimeSpan.FromMilliseconds(50)));
        using var call = interceptor.AsyncClientStreamingCall(
            Context(),
            context => new AsyncClientStreamingCall<Request, Reply>(
                new BlockingWriter(context.Options.CancellationToken, cancelled),
                Task.FromResult(new Reply()),
                Task.FromResult(new Metadata()),
                static () => Status.DefaultSuccess,
                static () => new Metadata(),
                static () => { }));

        _ = await Assert.That(async () =>
                await call.RequestStream.WriteAsync(new Request())
                    .WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<OperationCanceledException>();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static ClientInterceptorContext<Request, Reply> Context()
    {
        var method = new Method<Request, Reply>(
            MethodType.ClientStreaming,
            "kevlar.tests.NetStandard",
            "ClientStreaming",
            Marshallers.Create(static _ => [], static _ => new Request()),
            Marshallers.Create(static _ => [], static _ => new Reply()));
        return new ClientInterceptorContext<Request, Reply>(method, host: null, default);
    }

    private sealed class BlockingWriter(
        CancellationToken lifetimeToken,
        TaskCompletionSource cancelled) : IClientStreamWriter<Request>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task CompleteAsync() => Task.CompletedTask;

        public Task WriteAsync(Request message)
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lifetimeToken.Register(() =>
            {
                cancelled.TrySetResult();
                completion.TrySetException(
                    new RpcException(new Status(StatusCode.Cancelled, "cancelled")));
            });
            return completion.Task;
        }
    }

    private sealed class Request;

    private sealed class Reply;
}
