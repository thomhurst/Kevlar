using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;
using Polly.Timeout;

namespace Kevlar.Benchmarks;

/// <summary>
/// Timeout strategy happy path: the timeout never fires, so this measures the cost of
/// arming and disarming the cancellation plumbing per execution.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class TimeoutBenchmarks
{
    private static readonly Shield KevlarTimeout = Shield.Timeout(TimeSpan.FromSeconds(10));

    private static readonly ResiliencePipeline PollyTimeout = new ResiliencePipelineBuilder()
        .AddTimeout(new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(10) })
        .Build();

    [BenchmarkCategory("HappyPath"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_HappyPath() => KevlarTimeout.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("HappyPath"), Benchmark]
    public ValueTask<int> Polly_HappyPath() => PollyTimeout.ExecuteAsync(static _ => new ValueTask<int>(42));
}
