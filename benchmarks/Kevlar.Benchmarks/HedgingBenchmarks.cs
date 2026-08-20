using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;
using Polly.Hedging;

namespace Kevlar.Benchmarks;

/// <summary>
/// Hedging happy path: the primary attempt completes synchronously, so the hedge timer
/// never fires. Measures per-call setup cost of the hedging machinery.
/// Polly only offers hedging on typed pipelines, so both sides use typed pipelines.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class HedgingBenchmarks
{
    private static readonly Shield<int> KevlarHedge = Shield.For<int>().Hedge(2, TimeSpan.FromSeconds(10));

    private static readonly ResiliencePipeline<int> PollyHedge = new ResiliencePipelineBuilder<int>()
        .AddHedging(new HedgingStrategyOptions<int> { MaxHedgedAttempts = 1, Delay = TimeSpan.FromSeconds(10) })
        .Build();

    [BenchmarkCategory("PrimaryWins"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_PrimaryWins() => KevlarHedge.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("PrimaryWins"), Benchmark]
    public ValueTask<int> Polly_PrimaryWins() => PollyHedge.ExecuteAsync(static _ => new ValueTask<int>(42));
}
