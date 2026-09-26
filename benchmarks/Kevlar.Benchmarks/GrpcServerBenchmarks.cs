using BenchmarkDotNet.Attributes;
using Grpc.Core;
using Kevlar.Extensions.Grpc;

namespace Kevlar.Benchmarks;

/// <summary>Server admission overhead compared with invoking the same completed handler directly.</summary>
[MemoryDiagnoser]
public class GrpcServerBenchmarks
{
    private readonly object _request = new();
    private readonly ServerCallContext _context = new BenchmarkContext();
    private readonly Task<object> _response = Task.FromResult(new object());
    private UnaryServerMethod<object, object> _handler = null!;
    private ShieldServerInterceptor _empty = null!;
    private ShieldServerInterceptor _limited = null!;
    private ShieldServerInterceptor _partitioned = null!;
    private PartitionedShield<string> _partitions = null!;

    /// <summary>Creates reusable shields, handler, and a warmed method partition.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _handler = (_, _) => _response;
        _empty = new ShieldServerInterceptor(Shield.Empty);
        _limited = new ShieldServerInterceptor(Shield.ConcurrencyLimit(100, queueLimit: 0));
        _partitions = new PartitionedShield<string>(_ => Shield.ConcurrencyLimit(100, queueLimit: 0));
        _ = _partitions.GetShield(_context.Method);
        _partitioned = new ShieldServerInterceptor(_partitions);
    }

    /// <summary>Disposes the owned partition provider.</summary>
    [GlobalCleanup]
    public void Cleanup() => _partitions.Dispose();

    /// <summary>Existing direct-handler behavior without the new interceptor.</summary>
    [Benchmark(Baseline = true)]
    public Task<object> Direct() => _handler(_request, _context);

    /// <summary>Server interception with no strategies.</summary>
    [Benchmark]
    public Task<object> EmptyShield() => _empty.UnaryServerHandler(_request, _context, _handler);

    /// <summary>Server interception with shared concurrency admission.</summary>
    [Benchmark]
    public Task<object> ConcurrencyLimit() => _limited.UnaryServerHandler(_request, _context, _handler);

    /// <summary>Server interception with cached per-method concurrency admission.</summary>
    [Benchmark]
    public Task<object> MethodPartition() => _partitioned.UnaryServerHandler(_request, _context, _handler);

    private sealed class BenchmarkContext : ServerCallContext
    {
        protected override string MethodCore => "/benchmarks.Server/Unary";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:5000";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => default;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => null!;
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotSupportedException();
    }
}
