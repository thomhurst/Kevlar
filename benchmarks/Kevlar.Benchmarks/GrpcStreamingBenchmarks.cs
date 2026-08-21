using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kevlar.Extensions.Grpc;
using System.Runtime.CompilerServices;

namespace Kevlar.Benchmarks;

/// <summary>Streaming client happy-path overhead over completed in-memory operations.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class GrpcStreamingBenchmarks
{
    private static readonly BenchmarkRequest Request = new();
    private static readonly CompletedReader Reader = new();
    private static readonly CompletedWriter Writer = new();
    private static readonly ShieldStreamingClientInterceptor Interceptor = new(Shield.Empty);
    private static readonly Method<BenchmarkRequest, BenchmarkResponse> ServerMethod = new(
        MethodType.ServerStreaming,
        "benchmarks.Grpc",
        "ServerStream",
        Marshallers.Create(static _ => [], static _ => Request),
        Marshallers.Create(static _ => [], static _ => new BenchmarkResponse()));
    private static readonly Method<BenchmarkRequest, BenchmarkResponse> ClientMethod = new(
        MethodType.ClientStreaming,
        "benchmarks.Grpc",
        "ClientStream",
        Marshallers.Create(static _ => [], static _ => Request),
        Marshallers.Create(static _ => [], static _ => new BenchmarkResponse()));
    private static readonly AsyncClientStreamingCall<BenchmarkRequest, BenchmarkResponse> ShieldedClientCall =
        Interceptor.AsyncClientStreamingCall(
            new ClientInterceptorContext<BenchmarkRequest, BenchmarkResponse>(
                ClientMethod,
                host: null,
                default),
            static _ => ClientCall());

    /// <summary>Reads a completed first server-streaming item directly.</summary>
    [BenchmarkCategory("ServerStreamingFirstMove"), Benchmark(Baseline = true)]
    public Task<bool> ServerDirect() =>
        ServerCall().ResponseStream.MoveNext(CancellationToken.None);

    /// <summary>Establishes and reads a completed first item through the streaming interceptor.</summary>
    [BenchmarkCategory("ServerStreamingFirstMove"), Benchmark]
    public Task<bool> ServerShielded() => Interceptor.AsyncServerStreamingCall(
            Request,
            new ClientInterceptorContext<BenchmarkRequest, BenchmarkResponse>(
                ServerMethod,
                host: null,
                default),
            static (_, _) => ServerCall())
        .ResponseStream.MoveNext(CancellationToken.None);

    /// <summary>Writes a completed request-streaming item directly.</summary>
    [BenchmarkCategory("ClientStreamingWrite"), Benchmark(Baseline = true)]
    public Task WriteDirect() => Writer.WriteAsync(Request);

    /// <summary>Writes a completed request-streaming item through the streaming interceptor.</summary>
    [BenchmarkCategory("ClientStreamingWrite"), Benchmark]
    public Task WriteShielded() => ShieldedClientCall.RequestStream.WriteAsync(Request);

    private static AsyncServerStreamingCall<BenchmarkResponse> ServerCall() => new(
        Reader,
        Task.FromResult(new Metadata()),
        static () => Status.DefaultSuccess,
        static () => new Metadata(),
        static () => { });

    private static AsyncClientStreamingCall<BenchmarkRequest, BenchmarkResponse> ClientCall() => new(
        Writer,
        Task.FromResult(new BenchmarkResponse()),
        Task.FromResult(new Metadata()),
        static () => Status.DefaultSuccess,
        static () => new Metadata(),
        static () => { });

    private sealed class CompletedReader : IAsyncStreamReader<BenchmarkResponse>
    {
        public BenchmarkResponse Current { get; } = new();

        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class CompletedWriter : IClientStreamWriter<BenchmarkRequest>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task CompleteAsync() => Task.CompletedTask;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task WriteAsync(BenchmarkRequest message) => Task.CompletedTask;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task WriteAsync(BenchmarkRequest message, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class BenchmarkRequest;

    private sealed class BenchmarkResponse;
}
