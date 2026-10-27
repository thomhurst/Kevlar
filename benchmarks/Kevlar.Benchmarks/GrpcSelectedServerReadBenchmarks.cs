using BenchmarkDotNet.Attributes;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kevlar.Extensions.Grpc;

namespace Kevlar.Benchmarks;

/// <summary>Measures completed reads after server-stream selection with call and read cancellation.</summary>
[MemoryDiagnoser]
public class GrpcSelectedServerReadBenchmarks
{
    private CancellationTokenSource _caller = null!;
    private CancellationTokenSource _read = null!;
    private AsyncServerStreamingCall<Message> _call = null!;
    private CancellationToken _readToken;

    /// <summary>Whether selected reads pass through an operation concurrency shield.</summary>
    [Params(false, true)]
    public bool UseOperationShield { get; set; }

    /// <summary>Whether reads supply a separate live cancellation token.</summary>
    [Params(false, true)]
    public bool UseReadToken { get; set; }

    /// <summary>Establishes and selects the call outside the measured read operation.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _caller = new CancellationTokenSource();
        _read = new CancellationTokenSource();
        _readToken = UseReadToken ? _read.Token : CancellationToken.None;
        var interceptor = UseOperationShield
            ? new ShieldStreamingClientInterceptor(Shield.Empty, Shield.ConcurrencyLimit(1))
            : new ShieldStreamingClientInterceptor(Shield.Retry(1, Backoff.None));
        var method = new Method<Message, Message>(
            MethodType.ServerStreaming,
            "benchmarks.SelectedRead",
            "Read",
            Marshallers.Create(static _ => [], static _ => new Message()),
            Marshallers.Create(static _ => [], static _ => new Message()));
        _call = interceptor.AsyncServerStreamingCall(
            new Message(),
            new ClientInterceptorContext<Message, Message>(
                method, host: null, new CallOptions(cancellationToken: _caller.Token)),
            static (_, _) => new AsyncServerStreamingCall<Message>(
                new CompletedReader(),
                Task.FromResult(new Metadata()),
                static () => Status.DefaultSuccess,
                static () => new Metadata(),
                static () => { }));
        _ = await _call.ResponseHeadersAsync;
    }

    /// <summary>Reads a synchronously completed item from the selected stream.</summary>
    [Benchmark]
    public Task<bool> MoveNext() => _call.ResponseStream.MoveNext(_readToken);

    /// <summary>Releases call lifetime and cancellation registrations after measurement.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _call.Dispose();
        _read.Dispose();
        _caller.Dispose();
    }

    private sealed class Message;

    private sealed class CompletedReader : IAsyncStreamReader<Message>
    {
        public Message Current { get; } = new();

        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
