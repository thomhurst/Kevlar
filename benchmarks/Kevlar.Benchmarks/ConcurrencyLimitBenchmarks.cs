using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;

namespace Kevlar.Benchmarks;

/// <summary>
/// Concurrency limit uncontended path: a single caller against a large permit count, so
/// no queueing ever happens. Measures the acquire/release cost per call.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ConcurrencyLimitBenchmarks
{
    private static readonly Shield KevlarConcurrency = Shield.ConcurrencyLimit(1024);

    private static readonly ResiliencePipeline PollyConcurrency = new ResiliencePipelineBuilder()
        .AddConcurrencyLimiter(1024)
        .Build();

    [BenchmarkCategory("Uncontended"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_Uncontended() => KevlarConcurrency.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Uncontended"), Benchmark]
    public ValueTask<int> Polly_Uncontended() => PollyConcurrency.ExecuteAsync(static _ => new ValueTask<int>(42));
}
