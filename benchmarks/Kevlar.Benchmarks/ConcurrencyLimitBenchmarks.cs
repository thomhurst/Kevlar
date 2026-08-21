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
    private static readonly Shield KevlarConcurrencyWithHooks = Shield.ConcurrencyLimit(options =>
    {
        options.MaxConcurrency = 1024;
        options.OnRejected = static _ => { };
        options.OnRejectedAsync = static _ => ValueTask.CompletedTask;
    });

    private static readonly ResiliencePipeline PollyConcurrency = new ResiliencePipelineBuilder()
        .AddConcurrencyLimiter(1024)
        .Build();

    [BenchmarkCategory("Uncontended"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_Uncontended() => KevlarConcurrency.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Uncontended"), Benchmark]
    public ValueTask<int> Polly_Uncontended() => PollyConcurrency.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Uncontended"), Benchmark]
    public ValueTask<int> Kevlar_WithHooks_Uncontended() =>
        KevlarConcurrencyWithHooks.ExecuteAsync(static _ => new ValueTask<int>(42));
}
