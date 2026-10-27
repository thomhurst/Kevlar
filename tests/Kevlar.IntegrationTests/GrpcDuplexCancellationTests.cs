using Grpc.Core;
using Grpc.Core.Interceptors;
using Kevlar.Extensions.Grpc;

namespace Kevlar.IntegrationTests;

public class GrpcDuplexCancellationTests
{
    [Test]
    [Arguments(false, false, true)]
    [Arguments(true, false, true)]
    [Arguments(false, true, true)]
    [Arguments(false, false, false)]
    [Arguments(true, false, false)]
    public async Task Queued_Read_Observes_Both_Tokens_And_Releases_Queue_Capacity(
        bool disposeCall,
        bool cancelRead,
        bool cancellableRead)
    {
        using var callerCancellation = new CancellationTokenSource();
        using var readCancellation = new CancellationTokenSource();
        using var probeCancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.ConcurrencyLimit(maxConcurrency: 1, queueLimit: 1);
        var holder = shield.ExecuteAsync(async _ => await release.Task).AsTask();
        var moves = 0;
        CancellationToken lifetimeToken = default;
        using var call = CreateCall(shield, callerCancellation.Token, token =>
        {
            Interlocked.Increment(ref moves);
            return Task.FromResult(false);
        }, token => lifetimeToken = token);
        Task? probe = null;
        try
        {
            var move = call.ResponseStream.MoveNext(
                cancellableRead ? readCancellation.Token : CancellationToken.None);
            await Assert.That(move.IsCompleted).IsFalse();
            // The holder owns the only permit, and the read has reserved the only queue slot.
            _ = await Assert.That(async () => await shield.ExecuteAsync(static _ => default))
                .Throws<ConcurrencyLimitExceededException>();

            if (cancelRead)
            {
                readCancellation.Cancel();
            }
            else if (disposeCall)
            {
                call.Dispose();
            }
            else
            {
                callerCancellation.Cancel();
            }

            var exception = await Assert.That(async () =>
                    await move.WaitAsync(TimeSpan.FromSeconds(5)))
                .Throws<OperationCanceledException>();
            var expectedToken = cancelRead ? readCancellation.Token
                : disposeCall ? lifetimeToken : callerCancellation.Token;
            await Assert.That(exception!.CancellationToken).IsEqualTo(expectedToken);

            // A replacement can queue while the holder is still active: cancellation freed capacity.
            probe = shield.ExecuteAsync(static _ => default, probeCancellation.Token).AsTask();
            await Assert.That(probe.IsCompleted).IsFalse();
            release.TrySetResult();
            await holder.WaitAsync(TimeSpan.FromSeconds(5));
            await probe.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(moves).IsEqualTo(0);
        }
        finally
        {
            readCancellation.Cancel();
            probeCancellation.Cancel();
            release.TrySetResult();
            await holder.WaitAsync(TimeSpan.FromSeconds(5));
            if (probe is not null)
            {
                try
                {
                    await probe.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(true, false, false)]
    [Arguments(true, false, true)]
    [Arguments(false, true, false)]
    [Arguments(false, true, true)]
    public async Task Active_Read_Normalizes_Cancellation_With_Both_Tokens(
        bool disposeCall,
        bool cancelRead,
        bool transportCancellation)
    {
        using var callerCancellation = new CancellationTokenSource();
        using var readCancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken lifetimeToken = default;
        using var call = CreateCall(Shield.Empty, callerCancellation.Token, async token =>
        {
            started.TrySetResult();
            try
            {
                // A transport can observe call cancellation independently of the read token.
                var transportToken = transportCancellation && !cancelRead ? lifetimeToken : token;
                await Task.Delay(Timeout.InfiniteTimeSpan, transportToken);
                return false;
            }
            catch (OperationCanceledException) when (transportCancellation)
            {
                throw new RpcException(new Status(StatusCode.Cancelled, "cancelled"));
            }
        }, token => lifetimeToken = token);
        var move = call.ResponseStream.MoveNext(readCancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (cancelRead)
            {
                readCancellation.Cancel();
            }
            else if (disposeCall)
            {
                call.Dispose();
            }
            else
            {
                callerCancellation.Cancel();
            }

            var exception = await Assert.That(async () =>
                    await move.WaitAsync(TimeSpan.FromSeconds(5)))
                .Throws<OperationCanceledException>();
            var expectedToken = cancelRead ? readCancellation.Token
                : disposeCall ? lifetimeToken : callerCancellation.Token;
            await Assert.That(exception!.CancellationToken).IsEqualTo(expectedToken);
            if (transportCancellation)
            {
                await Assert.That(exception.GetBaseException()).IsTypeOf<RpcException>();
            }
        }
        finally
        {
            readCancellation.Cancel();
            try
            {
                await move.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Completed_Read_Disposes_Linked_Cancellation_Registrations(bool hasItem)
    {
        using var callerCancellation = new CancellationTokenSource();
        using var readCancellation = new CancellationTokenSource();
        CancellationToken operationToken = default;
        using var call = CreateCall(Shield.Empty, callerCancellation.Token, token =>
        {
            operationToken = token;
            return Task.FromResult(hasItem);
        });

        await Assert.That(await call.ResponseStream.MoveNext(readCancellation.Token)).IsEqualTo(hasItem);
        callerCancellation.Cancel();
        readCancellation.Cancel();

        await Assert.That(operationToken.CanBeCanceled).IsTrue();
        await Assert.That(operationToken.IsCancellationRequested).IsFalse();
    }

    private static AsyncDuplexStreamingCall<Request, Reply> CreateCall(
        Shield shield,
        CancellationToken callerToken,
        Func<CancellationToken, Task<bool>> move,
        Action<CancellationToken>? captureLifetime = null)
    {
        var method = new Method<Request, Reply>(
            MethodType.DuplexStreaming,
            "kevlar.tests.DuplexCancellation",
            "DuplexStreaming",
            Marshallers.Create(static _ => [], static _ => new Request()),
            Marshallers.Create(static _ => [], static _ => new Reply()));
        return new ShieldStreamingClientInterceptor(shield).AsyncDuplexStreamingCall(
            new ClientInterceptorContext<Request, Reply>(method, host: null,
                new CallOptions(cancellationToken: callerToken)),
            context =>
            {
                captureLifetime?.Invoke(context.Options.CancellationToken);
                return new AsyncDuplexStreamingCall<Request, Reply>(
                    new CompletedWriter(), new DelegateReader(move),
                    Task.FromResult(new Metadata()),
                    static () => Status.DefaultSuccess,
                    static () => new Metadata(),
                    static () => { });
            });
    }

    private sealed class DelegateReader(Func<CancellationToken, Task<bool>> move) :
        IAsyncStreamReader<Reply>
    {
        public Reply Current { get; } = new();

        public Task<bool> MoveNext(CancellationToken cancellationToken) => move(cancellationToken);
    }

    private sealed class CompletedWriter : IClientStreamWriter<Request>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task CompleteAsync() => Task.CompletedTask;

        public Task WriteAsync(Request message) => Task.CompletedTask;
    }

    private sealed class Request;

    private sealed class Reply;
}
