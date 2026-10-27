using BenchmarkDotNet.Attributes;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kevlar.Extensions.Grpc;

namespace Kevlar.Benchmarks;

/// <summary>Per-item duplex read overhead with and without a separate read cancellation token.</summary>
[MemoryDiagnoser]
public class GrpcDuplexStreamingBenchmarks
{
    private readonly CompletedReader _reader = new();
    private CancellationTokenSource _callerCancellation = null!;
    private CancellationTokenSource _readCancellation = null!;
    private AsyncDuplexStreamingCall<Request, Reply> _call = null!;
    private CancellationToken _readToken;

    /// <summary>Whether each read supplies a cancellable token distinct from the call token.</summary>
    [Params(false, true)]
    public bool CancellableRead { get; set; }

    /// <summary>Creates one reusable call and its cancellation sources outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _callerCancellation = new CancellationTokenSource();
        _readCancellation = new CancellationTokenSource();
        _readToken = CancellableRead ? _readCancellation.Token : CancellationToken.None;
        var method = new Method<Request, Reply>(
            MethodType.DuplexStreaming,
            "benchmarks.Grpc",
            "DuplexStreaming",
            Marshallers.Create(static _ => [], static _ => new Request()),
            Marshallers.Create(static _ => [], static _ => new Reply()));
        _call = new ShieldStreamingClientInterceptor(Shield.Empty).AsyncDuplexStreamingCall(
            new ClientInterceptorContext<Request, Reply>(method, host: null,
                new CallOptions(cancellationToken: _callerCancellation.Token)),
            _ => new AsyncDuplexStreamingCall<Request, Reply>(
                new CompletedWriter(), _reader, Task.FromResult(new Metadata()),
                static () => Status.DefaultSuccess,
                static () => new Metadata(),
                static () => { }));
    }

    /// <summary>Releases the reusable call and its cancellation sources.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _call.Dispose();
        _readCancellation.Dispose();
        _callerCancellation.Dispose();
    }

    /// <summary>Reads a completed item directly.</summary>
    [Benchmark(Baseline = true)]
    public Task<bool> Direct() => _reader.MoveNext(_readToken);

    /// <summary>Reads a completed item through the streaming interceptor.</summary>
    [Benchmark]
    public Task<bool> Shielded() => _call.ResponseStream.MoveNext(_readToken);

    private sealed class CompletedReader : IAsyncStreamReader<Reply>
    {
        public Reply Current { get; } = new();

        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(true);
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
