using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kevlar.Extensions.Grpc;

namespace Kevlar.Benchmarks;

/// <summary>Unary client happy-path overhead over a completed in-memory call invoker.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class GrpcUnaryBenchmarks
{
    private static readonly BenchmarkRequest Request = new();
    private static readonly Method<BenchmarkRequest, BenchmarkResponse> Method = new(
        MethodType.Unary,
        "benchmarks.Grpc",
        "Unary",
        Marshallers.Create(static _ => [], static _ => Request),
        Marshallers.Create(static _ => [], static _ => new BenchmarkResponse()));
    private static readonly CallInvoker DirectInvoker = new CompletedCallInvoker();
    private static readonly CallInvoker ShieldedInvoker =
        DirectInvoker.Intercept(new ShieldUnaryClientInterceptor(Shield.Empty));

    /// <summary>Executes a completed unary call without resilience.</summary>
    [BenchmarkCategory("UnaryHappyPath"), Benchmark(Baseline = true)]
    public Task Direct() =>
        DirectInvoker.AsyncUnaryCall(Method, host: null, default, Request).ResponseAsync;

    /// <summary>Executes a completed unary call through the shield interceptor.</summary>
    [BenchmarkCategory("UnaryHappyPath"), Benchmark]
    public Task Shielded() =>
        ShieldedInvoker.AsyncUnaryCall(Method, host: null, default, Request).ResponseAsync;

    private sealed class CompletedCallInvoker : CallInvoker
    {
        private static readonly BenchmarkResponse Response = new();

        private static readonly Task<BenchmarkResponse> ResponseTask = Task.FromResult(Response);
        private static readonly Task<Metadata> HeadersTask = Task.FromResult(new Metadata());

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request) =>
            (AsyncUnaryCall<TResponse>)(object)new AsyncUnaryCall<BenchmarkResponse>(
                ResponseTask,
                HeadersTask,
                static () => Status.DefaultSuccess,
                static () => new Metadata(),
                static () => { });

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

    private sealed class BenchmarkRequest;

    private sealed class BenchmarkResponse;
}
