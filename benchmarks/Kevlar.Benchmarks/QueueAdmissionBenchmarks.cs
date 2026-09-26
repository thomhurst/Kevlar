using BenchmarkDotNet.Attributes;

namespace Kevlar.Benchmarks;

/// <summary>Measures default admission paths with and without queue capacity.</summary>
[MemoryDiagnoser]
public class QueueAdmissionBenchmarks
{
    private readonly Shield _concurrency = Shield.ConcurrencyLimit(1024);
    private readonly Shield _concurrencyQueue = Shield.ConcurrencyLimit(1024, queueLimit: 10);
    private readonly Shield _rate = Shield.RateLimit(1_000_000_000, perWindow: TimeSpan.FromSeconds(1));
    private readonly Shield _rateQueue = Shield.RateLimit(options =>
    {
        options.Permits = 1_000_000_000;
        options.QueueLimit = 10;
    });

    [Benchmark]
    public ValueTask<int> Concurrency() => _concurrency.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> ConcurrencyQueue() => _concurrencyQueue.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> Rate() => _rate.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> RateQueue() => _rateQueue.ExecuteAsync(static _ => new ValueTask<int>(42));
}
