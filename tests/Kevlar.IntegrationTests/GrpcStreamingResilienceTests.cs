using Grpc.Core;
using Grpc.Core.Interceptors;
using Kevlar.Extensions.Grpc;

namespace Kevlar.IntegrationTests;

[NotInParallel]
public class GrpcStreamingResilienceTests
{
    private static readonly Method<StreamRequest, StreamReply> ServerStreamingMethod =
        CreateMethod(MethodType.ServerStreaming);
    private static readonly Method<StreamRequest, StreamReply> ClientStreamingMethod =
        CreateMethod(MethodType.ClientStreaming);
    private static readonly Method<StreamRequest, StreamReply> DuplexStreamingMethod =
        CreateMethod(MethodType.DuplexStreaming);

    [Test]
    public async Task Server_Stream_Retries_Before_First_Item_And_Selects_Final_Metadata()
    {
        var attempts = 0;
        var disposed = 0;
        var interceptor = new ShieldStreamingClientInterceptor(
            GrpcShield.WhenTransient().Retry(1, Backoff.None));
        using var call = interceptor.AsyncServerStreamingCall(
            new StreamRequest(),
            Context(ServerStreamingMethod),
            (_, _) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                var reader = attempt == 1
                    ? Reader((_, _) => Task.FromException<(bool, StreamReply?)>(Transient()))
                    : Reader((_, _) => Task.FromResult<(bool, StreamReply?)>(
                        (true, new StreamReply { Attempt = attempt })));
                return ServerCall(
                    reader,
                    () => Interlocked.Increment(ref disposed),
                    headers: new Metadata { { "attempt", attempt.ToString() } },
                    trailers: new Metadata { { "selected", attempt.ToString() } });
            });

        var hasNext = await call.ResponseStream.MoveNext(CancellationToken.None);

        await Assert.That(hasNext).IsTrue();
        await Assert.That(call.ResponseStream.Current.Attempt).IsEqualTo(2);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(disposed).IsEqualTo(1);
        await Assert.That((await call.ResponseHeadersAsync).GetValue("attempt")).IsEqualTo("2");
        await Assert.That(call.GetTrailers().GetValue("selected")).IsEqualTo("2");
    }

    [Test]
    public async Task Server_Stream_Does_Not_Retry_After_First_Item()
    {
        var attempts = 0;
        var moves = 0;
        var interceptor = new ShieldStreamingClientInterceptor(
            GrpcShield.WhenTransient().Retry(3, Backoff.None));
        using var call = interceptor.AsyncServerStreamingCall(
            new StreamRequest(),
            Context(ServerStreamingMethod),
            (_, _) =>
            {
                Interlocked.Increment(ref attempts);
                return ServerCall(Reader((_, _) =>
                {
                    if (Interlocked.Increment(ref moves) == 1)
                    {
                        return Task.FromResult<(bool, StreamReply?)>(
                            (true, new StreamReply { Attempt = 1 }));
                    }

                    return Task.FromException<(bool, StreamReply?)>(Transient());
                }));
            });

        await Assert.That(await call.ResponseStream.MoveNext(CancellationToken.None)).IsTrue();
        _ = await Assert.That(async () =>
                await call.ResponseStream.MoveNext(CancellationToken.None))
            .Throws<RpcException>();

        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(moves).IsEqualTo(2);
    }

    [Test]
    public async Task Server_Headers_Commit_The_Stream_Before_Any_Item()
    {
        var attempts = 0;
        var interceptor = new ShieldStreamingClientInterceptor(
            GrpcShield.WhenTransient().Retry(2, Backoff.None));
        using var call = interceptor.AsyncServerStreamingCall(
            new StreamRequest(),
            Context(ServerStreamingMethod),
            (_, _) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                return ServerCall(
                    Reader((_, _) => Task.FromException<(bool, StreamReply?)>(Transient())),
                    headers: new Metadata { { "attempt", attempt.ToString() } });
            });

        await Assert.That((await call.ResponseHeadersAsync).GetValue("attempt")).IsEqualTo("1");
        _ = await Assert.That(async () =>
                await call.ResponseStream.MoveNext(CancellationToken.None))
            .Throws<RpcException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Server_Stream_Retries_Header_Failure_Before_Commit()
    {
        var attempts = 0;
        var disposed = 0;
        var interceptor = new ShieldStreamingClientInterceptor(
            GrpcShield.WhenTransient().Retry(1, Backoff.None));
        using var call = interceptor.AsyncServerStreamingCall(
            new StreamRequest(),
            Context(ServerStreamingMethod),
            (_, _) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                return ServerCall(
                    Reader((_, _) => Task.FromResult<(bool, StreamReply?)>((false, null))),
                    () => Interlocked.Increment(ref disposed),
                    responseHeaders: attempt == 1
                        ? Task.FromException<Metadata>(Transient())
                        : Task.FromResult(new Metadata { { "attempt", "2" } }));
            });

        var headers = await call.ResponseHeadersAsync;

        await Assert.That(headers.GetValue("attempt")).IsEqualTo("2");
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(disposed).IsEqualTo(1);
    }

    [Test]
    public async Task Server_Stream_Hedge_Cancels_And_Disposes_The_Loser()
    {
        var attempts = 0;
        var disposed = 0;
        var loserCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var interceptor = new ShieldStreamingClientInterceptor(
            Shield.Hedge(2, TimeSpan.Zero));
        using var call = interceptor.AsyncServerStreamingCall(
            new StreamRequest(),
            Context(ServerStreamingMethod),
            (_, _) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt == 1)
                {
                    return ServerCall(
                        Reader(async (_, token) =>
                        {
                            try
                            {
                                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                                return (false, null);
                            }
                            catch (OperationCanceledException)
                            {
                                loserCancelled.TrySetResult();
                                throw;
                            }
                        }),
                        () => Interlocked.Increment(ref disposed));
                }

                return ServerCall(Reader((_, _) =>
                    Task.FromResult<(bool, StreamReply?)>(
                        (true, new StreamReply { Attempt = attempt }))));
            });

        await Assert.That(await call.ResponseStream.MoveNext(CancellationToken.None)).IsTrue();

        await loserCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(call.ResponseStream.Current.Attempt).IsEqualTo(2);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(disposed).IsEqualTo(1);
    }

    [Test]
    public async Task Server_Stream_AtMostOnce_Shield_Applies_To_Each_MoveNext()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interceptor = new ShieldStreamingClientInterceptor(
            Shield.Timeout(TimeSpan.FromMilliseconds(50)));
        using var call = interceptor.AsyncServerStreamingCall(
            new StreamRequest(),
            Context(ServerStreamingMethod),
            (_, _) => ServerCall(Reader((move, token) =>
            {
                if (move == 1)
                {
                    return Task.FromResult<(bool, StreamReply?)>(
                        (true, new StreamReply { Attempt = 1 }));
                }

                var completion = new TaskCompletionSource<(bool, StreamReply?)>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                token.Register(() =>
                {
                    cancelled.TrySetResult();
                    completion.TrySetCanceled(token);
                });
                return completion.Task;
            })));

        await Assert.That(await call.ResponseStream.MoveNext(CancellationToken.None)).IsTrue();
        _ = await Assert.That(async () =>
                await call.ResponseStream.MoveNext(CancellationToken.None))
            .Throws<TimeoutExceededException>();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Separate_Shields_Retry_Establishment_And_Timeout_Later_Reads()
    {
        var attempts = 0;
        var cancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var interceptor = new ShieldStreamingClientInterceptor(
            GrpcShield.WhenTransient().Retry(1, Backoff.None),
            Shield.Timeout(TimeSpan.FromMilliseconds(50)));
        using var call = interceptor.AsyncServerStreamingCall(
            new StreamRequest(),
            Context(ServerStreamingMethod),
            (_, _) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    return ServerCall(Reader((_, _) =>
                        Task.FromException<(bool, StreamReply?)>(Transient())));
                }

                return ServerCall(Reader((move, token) =>
                {
                    if (move == 1)
                    {
                        return Task.FromResult<(bool, StreamReply?)>(
                            (true, new StreamReply { Attempt = 2 }));
                    }

                    var completion = new TaskCompletionSource<(bool, StreamReply?)>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    token.Register(() =>
                    {
                        cancelled.TrySetResult();
                        completion.TrySetCanceled(token);
                    });
                    return completion.Task;
                }));
            });

        await Assert.That(await call.ResponseStream.MoveNext(CancellationToken.None)).IsTrue();
        _ = await Assert.That(async () =>
                await call.ResponseStream.MoveNext(CancellationToken.None))
            .Throws<TimeoutExceededException>();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Client_Stream_Response_Leaves_Metadata_Available_Until_Disposed()
    {
        var disposed = 0;
        var interceptor = new ShieldStreamingClientInterceptor(Shield.Empty);
        var call = interceptor.AsyncClientStreamingCall(
            Context(ClientStreamingMethod),
            _ => ClientCall(
                new DelegateWriter(),
                Task.FromResult(new StreamReply { Attempt = 1 }),
                () => Interlocked.Increment(ref disposed),
                new Metadata { { "terminal", "true" } }));

        var response = await call.ResponseAsync;

        await Assert.That(response.Attempt).IsEqualTo(1);
        await Assert.That(disposed).IsEqualTo(0);
        await Assert.That(call.GetStatus()).IsEqualTo(Status.DefaultSuccess);
        await Assert.That(call.GetTrailers().GetValue("terminal")).IsEqualTo("true");
        call.Dispose();
        call.Dispose();
        await Assert.That(disposed).IsEqualTo(1);
    }

    [Test]
    public async Task Client_Stream_Write_Timeout_Cancels_Underlying_Call_Without_Replay()
    {
        var writes = 0;
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new DelegateWriter((_, token) =>
        {
            Interlocked.Increment(ref writes);
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() =>
            {
                cancelled.TrySetResult();
                completion.TrySetCanceled(token);
            });
            return completion.Task;
        });
        var interceptor = new ShieldStreamingClientInterceptor(
            Shield.Timeout(TimeSpan.FromMilliseconds(50)));
        using var call = interceptor.AsyncClientStreamingCall(
            Context(ClientStreamingMethod),
            _ => ClientCall(writer, Task.FromResult(new StreamReply())));

        _ = await Assert.That(async () =>
                await call.RequestStream.WriteAsync(new StreamRequest()))
            .Throws<TimeoutExceededException>();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(writes).IsEqualTo(1);
    }

    [Test]
    public async Task Duplex_Stream_Shields_Reads_And_Writes_Without_Replay()
    {
        var writes = 0;
        var moves = 0;
        var interceptor = new ShieldStreamingClientInterceptor(Shield.Empty);
        using var call = interceptor.AsyncDuplexStreamingCall(
            Context(DuplexStreamingMethod),
            _ => DuplexCall(
                new DelegateWriter((_, _) =>
                {
                    Interlocked.Increment(ref writes);
                    return Task.CompletedTask;
                }),
                Reader((_, _) =>
                {
                    Interlocked.Increment(ref moves);
                    return Task.FromResult<(bool, StreamReply?)>(
                        (true, new StreamReply { Attempt = 7 }));
                })));

        await call.RequestStream.WriteAsync(new StreamRequest());
        var hasNext = await call.ResponseStream.MoveNext(CancellationToken.None);

        await Assert.That(hasNext).IsTrue();
        await Assert.That(call.ResponseStream.Current.Attempt).IsEqualTo(7);
        await Assert.That(writes).IsEqualTo(1);
        await Assert.That(moves).IsEqualTo(1);
    }

    [Test]
    public async Task Request_Streaming_Rejects_Strategies_That_Can_Replay_Operations()
    {
        var interceptor = new ShieldStreamingClientInterceptor(
            Shield.When(static _ => true).Retry(1, Backoff.None));

        _ = await Assert.That(() => interceptor.AsyncClientStreamingCall(
                Context(ClientStreamingMethod),
                _ => ClientCall(new DelegateWriter(), Task.FromResult(new StreamReply()))))
            .Throws<NotSupportedException>();
        _ = await Assert.That(() => interceptor.AsyncDuplexStreamingCall(
                Context(DuplexStreamingMethod),
                _ => DuplexCall(new DelegateWriter(), Reader((_, _) =>
                    Task.FromResult<(bool, StreamReply?)>((false, null))))))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task Disposing_Server_Stream_Cancels_And_Disposes_Active_Attempt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = 0;
        var interceptor = new ShieldStreamingClientInterceptor(Shield.Empty);
        var call = interceptor.AsyncServerStreamingCall(
            new StreamRequest(),
            Context(ServerStreamingMethod),
            (_, _) => ServerCall(
                Reader((_, token) =>
                {
                    started.TrySetResult();
                    var completion = new TaskCompletionSource<(bool, StreamReply?)>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    token.Register(() =>
                    {
                        cancelled.TrySetResult();
                        completion.TrySetCanceled(token);
                    });
                    return completion.Task;
                }),
                () => Interlocked.Increment(ref disposed)));
        var move = call.ResponseStream.MoveNext(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        call.Dispose();
        call.Dispose();

        _ = await Assert.That(async () => await move).Throws<OperationCanceledException>();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(disposed).IsEqualTo(1);
    }

    [Test]
    public async Task Expired_Deadline_Stops_Unconditional_Server_Stream_Retry()
    {
        var expected = new RpcException(
            new Status(StatusCode.DeadlineExceeded, "deadline"));
        var attempts = 0;
        var interceptor = new ShieldStreamingClientInterceptor(
            Shield.When(static _ => true).RetryForever(Backoff.None));
        using var call = interceptor.AsyncServerStreamingCall(
            new StreamRequest(),
            Context(
                ServerStreamingMethod,
                new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(50))),
            (_, _) =>
            {
                Interlocked.Increment(ref attempts);
                return ServerCall(Reader(async (_, _) =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                    throw expected;
                }));
            });

        var actual = await Assert.That(async () =>
                await call.ResponseStream.MoveNext(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<RpcException>();

        await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Caller_Cancellation_On_Server_Stream_Preserves_Caller_Token()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interceptor = new ShieldStreamingClientInterceptor(Shield.Empty);
        using var call = interceptor.AsyncServerStreamingCall(
            new StreamRequest(),
            Context(
                ServerStreamingMethod,
                new CallOptions(cancellationToken: cancellation.Token)),
            (_, _) => ServerCall(Reader(async (_, token) =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return (false, null);
                }
                catch (OperationCanceledException)
                {
                    throw new RpcException(new Status(StatusCode.Cancelled, "cancelled"));
                }
            })));
        var move = call.ResponseStream.MoveNext(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        var exception = await Assert.That(async () => await move)
            .Throws<OperationCanceledException>();

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task Streaming_Interceptor_Rejects_Null_Shield()
    {
        _ = await Assert.That(() => new ShieldStreamingClientInterceptor(null!))
            .Throws<ArgumentNullException>();
        _ = await Assert.That(() => new ShieldStreamingClientInterceptor(null!, Shield.Empty))
            .Throws<ArgumentNullException>();
        _ = await Assert.That(() => new ShieldStreamingClientInterceptor(Shield.Empty, null!))
            .Throws<ArgumentNullException>();
        _ = await Assert.That(() => new ShieldStreamingClientInterceptor(
                Shield.Empty,
                Shield.Retry(1, Backoff.None)))
            .Throws<ArgumentException>();
    }

    private static ClientInterceptorContext<StreamRequest, StreamReply> Context(
        Method<StreamRequest, StreamReply> method,
        CallOptions options = default) => new(method, host: null, options);

    private static Method<StreamRequest, StreamReply> CreateMethod(MethodType type) => new(
        type,
        "kevlar.tests.Streaming",
        type.ToString(),
        Marshallers.Create(static _ => [], static _ => new StreamRequest()),
        Marshallers.Create(static _ => [], static _ => new StreamReply()));

    private static RpcException Transient() =>
        new(new Status(StatusCode.Unavailable, "transient"));

    private static DelegateReader Reader(
        Func<int, CancellationToken, Task<(bool HasNext, StreamReply? Current)>> move) => new(move);

    private static AsyncServerStreamingCall<StreamReply> ServerCall(
        IAsyncStreamReader<StreamReply> reader,
        Action? dispose = null,
        Metadata? headers = null,
        Metadata? trailers = null,
        Task<Metadata>? responseHeaders = null) => new(
        reader,
        responseHeaders ?? Task.FromResult(headers ?? new Metadata()),
        static () => Status.DefaultSuccess,
        () => trailers ?? new Metadata(),
        dispose ?? NoOp);

    private static AsyncClientStreamingCall<StreamRequest, StreamReply> ClientCall(
        IClientStreamWriter<StreamRequest> writer,
        Task<StreamReply> response,
        Action? dispose = null,
        Metadata? trailers = null) => new(
        writer,
        response,
        Task.FromResult(new Metadata()),
        static () => Status.DefaultSuccess,
        () => trailers ?? new Metadata(),
        dispose ?? NoOp);

    private static AsyncDuplexStreamingCall<StreamRequest, StreamReply> DuplexCall(
        IClientStreamWriter<StreamRequest> writer,
        IAsyncStreamReader<StreamReply> reader) => new(
        writer,
        reader,
        Task.FromResult(new Metadata()),
        static () => Status.DefaultSuccess,
        static () => new Metadata(),
        NoOp);

    private static void NoOp()
    {
    }

    private sealed class DelegateReader(
        Func<int, CancellationToken, Task<(bool HasNext, StreamReply? Current)>> move) :
        IAsyncStreamReader<StreamReply>
    {
        private int _moves;

        public StreamReply Current { get; private set; } = null!;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            var result = await move(Interlocked.Increment(ref _moves), cancellationToken)
                .ConfigureAwait(false);
            if (result.HasNext)
            {
                Current = result.Current!;
            }

            return result.HasNext;
        }
    }

    private sealed class DelegateWriter(
        Func<StreamRequest, CancellationToken, Task>? write = null) :
        IClientStreamWriter<StreamRequest>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task CompleteAsync() => Task.CompletedTask;

        public Task WriteAsync(StreamRequest message) =>
            (write ?? CompletedWrite)(message, CancellationToken.None);

        public Task WriteAsync(StreamRequest message, CancellationToken cancellationToken) =>
            (write ?? CompletedWrite)(message, cancellationToken);

        private static Task CompletedWrite(StreamRequest _, CancellationToken __) =>
            Task.CompletedTask;
    }

    private sealed class StreamRequest;

    private sealed class StreamReply
    {
        public int Attempt { get; init; }
    }
}
