using Grpc.Core;
using Grpc.Core.Interceptors;
using Kevlar.Extensions.Grpc;

namespace Kevlar.Tests;

public class GrpcSelectedServerReadCancellationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly Method<Message, Message> Method = new(
        MethodType.ServerStreaming,
        "tests.SelectedRead",
        "Read",
        Marshallers.Create(static _ => [], static _ => new Message()),
        Marshallers.Create(static _ => [], static _ => new Message()));

    [Test]
    [Arguments(false, 0)]
    [Arguments(true, 0)]
    [Arguments(false, 1)]
    [Arguments(true, 1)]
    [Arguments(false, 2)]
    [Arguments(true, 2)]
    public async Task Queued_Read_Cancellation_Releases_Capacity_Without_Late_Transport_Read(
        bool selectWithFirstItem,
        int cancellationKind)
    {
        using var caller = new CancellationTokenSource();
        using var read = new CancellationTokenSource();
        var releaseHolder = NewGate();
        var shield = Shield.ConcurrencyLimit(maxConcurrency: 1, queueLimit: 1);
        var interceptor = new ShieldStreamingClientInterceptor(Shield.Empty, shield);
        var reads = 0;
        using var call = CreateCall(interceptor, caller.Token, new DelegateReader(_ =>
        {
            Interlocked.Increment(ref reads);
            return Task.FromResult(true);
        }));
        if (selectWithFirstItem)
        {
            await Assert.That(await call.ResponseStream.MoveNext(CancellationToken.None)).IsTrue();
        }
        else
        {
            _ = await call.ResponseHeadersAsync.WaitAsync(TestTimeout);
        }

        var initialReads = reads;
        var holder = shield.ExecuteAsync(async _ => await releaseHolder.Task).AsTask();
        Task<bool>? move = null;
        Task<int>? replacement = null;
        try
        {
            move = call.ResponseStream.MoveNext(read.Token);
            await Assert.That(move.IsCompleted).IsFalse();
            // The sole queue slot is occupied, independently of scheduling or timing.
            _ = await Assert.That(async () =>
                    await shield.ExecuteAsync(static _ => new ValueTask<int>(0)))
                .Throws<ConcurrencyLimitExceededException>();

            Cancel(cancellationKind, caller, read, call);
            var exception = await Assert.That(async () => await move.WaitAsync(TestTimeout))
                .Throws<OperationCanceledException>();
            await AssertVisibleToken(exception!, cancellationKind, caller.Token, read.Token);
            await Assert.That(reads).IsEqualTo(initialReads);

            // A new waiter fits before the original permit is released.
            replacement = shield.ExecuteAsync(static _ => new ValueTask<int>(42)).AsTask();
            await Assert.That(replacement.IsCompleted).IsFalse();
            releaseHolder.TrySetResult();
            await holder.WaitAsync(TestTimeout);
            await Assert.That(await replacement.WaitAsync(TestTimeout)).IsEqualTo(42);
            await Assert.That(reads).IsEqualTo(initialReads);
        }
        finally
        {
            read.Cancel();
            releaseHolder.TrySetResult();
            await holder.WaitAsync(TestTimeout);
            await ObserveCancellation(move);
            if (replacement is not null)
            {
                await replacement.WaitAsync(TestTimeout);
            }
        }
    }

    [Test]
    [Arguments(false, false, 0)]
    [Arguments(false, true, 0)]
    [Arguments(true, false, 0)]
    [Arguments(true, true, 0)]
    [Arguments(false, false, 1)]
    [Arguments(false, true, 1)]
    [Arguments(true, false, 1)]
    [Arguments(true, true, 1)]
    [Arguments(false, false, 2)]
    [Arguments(false, true, 2)]
    [Arguments(true, false, 2)]
    [Arguments(true, true, 2)]
    public async Task Active_Read_Observes_Both_Tokens_And_Normalizes_Cancellation(
        bool useOperationShield,
        bool rpcCancellation,
        int cancellationKind)
    {
        using var caller = new CancellationTokenSource();
        using var read = new CancellationTokenSource();
        var started = NewGate();
        var releaseRead = NewGate();
        // A retry shield has no per-operation shield: this also exercises direct reads.
        var interceptor = useOperationShield
            ? new ShieldStreamingClientInterceptor(Shield.Empty, Shield.ConcurrencyLimit(1))
            : new ShieldStreamingClientInterceptor(Shield.Retry(1, Backoff.None));
        var receivedToken = CancellationToken.None;
        using var call = CreateCall(interceptor, caller.Token, new DelegateReader(async token =>
        {
            receivedToken = token;
            started.TrySetResult();
            try
            {
                await releaseRead.Task.WaitAsync(TestTimeout, token);
                return false;
            }
            catch (OperationCanceledException) when (rpcCancellation)
            {
                throw new RpcException(new Status(StatusCode.Cancelled, "cancelled"));
            }
        }));
        _ = await call.ResponseHeadersAsync.WaitAsync(TestTimeout);
        var move = call.ResponseStream.MoveNext(read.Token);
        try
        {
            await started.Task.WaitAsync(TestTimeout);
            Cancel(cancellationKind, caller, read, call);
            var exception = await Assert.That(async () => await move.WaitAsync(TestTimeout))
                .Throws<OperationCanceledException>();
            await AssertVisibleToken(exception!, cancellationKind, caller.Token, read.Token);
            await Assert.That(receivedToken.IsCancellationRequested).IsTrue();
        }
        finally
        {
            read.Cancel();
            releaseRead.TrySetResult();
            await ObserveCancellation(move);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Successful_Read_Disposes_Its_Linked_Cancellation(bool useOperationShield)
    {
        using var caller = new CancellationTokenSource();
        using var read = new CancellationTokenSource();
        var interceptor = useOperationShield
            ? new ShieldStreamingClientInterceptor(Shield.Empty, Shield.ConcurrencyLimit(1))
            : new ShieldStreamingClientInterceptor(Shield.Retry(1, Backoff.None));
        var receivedToken = CancellationToken.None;
        using var call = CreateCall(interceptor, caller.Token, new DelegateReader(token =>
        {
            receivedToken = token;
            return Task.FromResult(true);
        }));
        _ = await call.ResponseHeadersAsync.WaitAsync(TestTimeout);

        var move = call.ResponseStream.MoveNext(read.Token);
        await Assert.That(move.IsCompletedSuccessfully).IsTrue();
        await Assert.That(await move).IsTrue();
        read.Cancel();
        caller.Cancel();

        // Disposal removes both upstream registrations after the read completes.
        await Assert.That(receivedToken.CanBeCanceled).IsTrue();
        await Assert.That(receivedToken.IsCancellationRequested).IsFalse();
    }

    private static AsyncServerStreamingCall<Message> CreateCall(
        ShieldStreamingClientInterceptor interceptor,
        CancellationToken callerToken,
        IAsyncStreamReader<Message> reader) => interceptor.AsyncServerStreamingCall(
        new Message(),
        new ClientInterceptorContext<Message, Message>(
            Method, host: null, new CallOptions(cancellationToken: callerToken)),
        (_, _) => new AsyncServerStreamingCall<Message>(
            reader,
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => new Metadata(),
            static () => { }));

    private static void Cancel(
        int cancellationKind,
        CancellationTokenSource caller,
        CancellationTokenSource read,
        AsyncServerStreamingCall<Message> call)
    {
        switch (cancellationKind)
        {
            case 0: caller.Cancel(); break;
            case 1: read.Cancel(); break;
            case 2: call.Dispose(); break;
            default: throw new ArgumentOutOfRangeException(nameof(cancellationKind));
        }
    }

    private static async Task AssertVisibleToken(
        OperationCanceledException exception,
        int cancellationKind,
        CancellationToken callerToken,
        CancellationToken readToken)
    {
        if (cancellationKind != 2)
        {
            await Assert.That(exception.CancellationToken)
                .IsEqualTo(cancellationKind == 0 ? callerToken : readToken);
        }
        await Assert.That(exception.CancellationToken.IsCancellationRequested).IsTrue();
    }

    private static async Task ObserveCancellation(Task? task)
    {
        if (task is null) { return; }
        try
        {
            await task.WaitAsync(TestTimeout);
        }
        catch (OperationCanceledException) { }
    }

    private static TaskCompletionSource NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Message;

    private sealed class DelegateReader(Func<CancellationToken, Task<bool>> move) : IAsyncStreamReader<Message>
    {
        public Message Current { get; } = new();

        public Task<bool> MoveNext(CancellationToken cancellationToken) => move(cancellationToken);
    }
}
