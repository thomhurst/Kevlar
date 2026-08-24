using Grpc.Core;
using Grpc.Core.Interceptors;
using Kevlar.Extensions.Grpc;

namespace Kevlar.NetStandard.Tests;

public class GrpcStreamingCancellationTests
{
    [Test]
    public async Task Client_Write_Timeout_Cancels_The_NetStandard_Call_Lifetime()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interceptor = new ShieldStreamingClientInterceptor(
            Shield.Timeout(TimeSpan.FromMilliseconds(50)));
        using var call = interceptor.AsyncClientStreamingCall(
            Context(),
            context => new AsyncClientStreamingCall<Request, Reply>(
                new BlockingWriter(context.Options.CancellationToken, started, cancelled),
                Task.FromResult(new Reply()),
                Task.FromResult(new Metadata()),
                static () => Status.DefaultSuccess,
                static () => new Metadata(),
                static () => { }));

        var write = call.RequestStream.WriteAsync(new Request());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _ = await Assert.That(async () => await write.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<TimeoutExceededException>();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Caller_Cancellation_Preserves_The_Call_Token_For_Writes()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetimeCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var interceptor = new ShieldStreamingClientInterceptor(Shield.Empty);
        using var call = interceptor.AsyncClientStreamingCall(
            Context(cancellation.Token),
            context => new AsyncClientStreamingCall<Request, Reply>(
                new BlockingWriter(
                    context.Options.CancellationToken,
                    started,
                    lifetimeCancelled),
                Task.FromResult(new Reply()),
                Task.FromResult(new Metadata()),
                static () => Status.DefaultSuccess,
                static () => new Metadata(),
                static () => { }));

        var write = call.RequestStream.WriteAsync(new Request());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var exception = await Assert.That(async () =>
                await write.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<OperationCanceledException>();

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
        await lifetimeCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Per_Write_Cancellation_Preserves_The_Operation_Token()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetimeCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var interceptor = new ShieldStreamingClientInterceptor(Shield.Empty);
        using var call = interceptor.AsyncClientStreamingCall(
            Context(),
            context => new AsyncClientStreamingCall<Request, Reply>(
                new BlockingWriter(
                    context.Options.CancellationToken,
                    started,
                    lifetimeCancelled),
                Task.FromResult(new Reply()),
                Task.FromResult(new Metadata()),
                static () => Status.DefaultSuccess,
                static () => new Metadata(),
                static () => { }));

        var write = call.RequestStream.WriteAsync(new Request(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var exception = await Assert.That(async () =>
                await write.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<OperationCanceledException>();

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
        await lifetimeCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static ClientInterceptorContext<Request, Reply> Context(
        CancellationToken cancellationToken = default)
    {
        var method = new Method<Request, Reply>(
            MethodType.ClientStreaming,
            "kevlar.tests.NetStandard",
            "ClientStreaming",
            Marshallers.Create(static _ => [], static _ => new Request()),
            Marshallers.Create(static _ => [], static _ => new Reply()));
        return new ClientInterceptorContext<Request, Reply>(
            method,
            host: null,
            new CallOptions(cancellationToken: cancellationToken));
    }

    private sealed class BlockingWriter(
        CancellationToken lifetimeToken,
        TaskCompletionSource started,
        TaskCompletionSource cancelled) : IClientStreamWriter<Request>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task CompleteAsync() => Task.CompletedTask;

        public Task WriteAsync(Request message) => WriteAsync(message, lifetimeToken);

        public async Task WriteAsync(Request message, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() =>
            {
                cancelled.TrySetResult();
                completion.TrySetException(
                    new RpcException(new Status(StatusCode.Cancelled, "cancelled")));
            });
            started.TrySetResult();
            await completion.Task.ConfigureAwait(false);
        }
    }

    private sealed class Request;

    private sealed class Reply;
}
