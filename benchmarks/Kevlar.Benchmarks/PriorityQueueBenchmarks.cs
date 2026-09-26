using BenchmarkDotNet.Attributes;

namespace Kevlar.Benchmarks;

/// <summary>Measures uncontended admission with priority queueing and queue deadlines enabled.</summary>
[MemoryDiagnoser]
public class PriorityQueueBenchmarks
{
    private readonly Shield _concurrency = Shield.ConcurrencyLimit(options =>
    {
        options.MaxConcurrency = 1024;
        options.QueueLimit = 10;
        options.QueueTimeout = TimeSpan.FromSeconds(1);
        options.UsePriorityQueue = true;
    });
    private readonly Shield _rate = Shield.RateLimit(options =>
    {
        options.Permits = 1_000_000_000;
        options.QueueLimit = 10;
        options.QueueTimeout = TimeSpan.FromSeconds(1);
        options.UsePriorityQueue = true;
    });

    [Benchmark]
    public ValueTask<int> Concurrency() => _concurrency.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> Rate() => _rate.ExecuteAsync(static _ => new ValueTask<int>(42));
}
