using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;
using Polly.Fallback;

namespace Kevlar.Benchmarks;

/// <summary>
/// Fallback strategy: the pass-through path (execution succeeds, fallback unused) and
/// the triggered path (execution throws, fallback value substituted).
/// Polly only offers fallback on typed pipelines, so both sides use typed pipelines.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FallbackBenchmarks
{
    private static readonly InvalidOperationException PrimaryError = new("primary failed");

    private static readonly Shield<int> KevlarFallback = Shield.For<int>()
        .When<InvalidOperationException>()
        .Fallback(7);

    private static readonly Shield<int> KevlarSyncNotification = Shield.For<int>()
        .When<InvalidOperationException>()
        .Fallback(7, static _ => { });

    private static readonly Shield<int> KevlarCompletedAsyncNotification = Shield.For<int>()
        .When<InvalidOperationException>()
        .FallbackWithNotifications(
            7,
            new FallbackOptions<int>
            {
                OnFallbackAsync = static _ => ValueTask.CompletedTask,
            });

    private static readonly Shield<int> KevlarYieldingAsyncNotification = Shield.For<int>()
        .When<InvalidOperationException>()
        .FallbackWithNotifications(
            7,
            new FallbackOptions<int>
            {
                OnFallbackAsync = static async _ => await Task.Yield(),
            });

    private static readonly ResiliencePipeline<int> PollyFallback = new ResiliencePipelineBuilder<int>()
        .AddFallback(new FallbackStrategyOptions<int>
        {
            ShouldHandle = new PredicateBuilder<int>().Handle<InvalidOperationException>(),
            FallbackAction = static _ => Outcome.FromResultAsValueTask(7),
        })
        .Build();

    [BenchmarkCategory("PassThrough"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_PassThrough() => KevlarFallback.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("PassThrough"), Benchmark]
    public ValueTask<int> Polly_PassThrough() => PollyFallback.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Triggered"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_Triggered() => KevlarFallback.ExecuteAsync(static _ => throw PrimaryError);

    [BenchmarkCategory("Triggered"), Benchmark]
    public ValueTask<int> Polly_Triggered() => PollyFallback.ExecuteAsync(static ValueTask<int> (_) => throw PrimaryError);

    [BenchmarkCategory("Notifications"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_NoNotification() => KevlarFallback.ExecuteAsync(static _ => throw PrimaryError);

    [BenchmarkCategory("Notifications"), Benchmark]
    public ValueTask<int> Kevlar_SyncNotification() => KevlarSyncNotification.ExecuteAsync(static _ => throw PrimaryError);

    [BenchmarkCategory("Notifications"), Benchmark]
    public ValueTask<int> Kevlar_CompletedAsyncNotification() =>
        KevlarCompletedAsyncNotification.ExecuteAsync(static _ => throw PrimaryError);

    [BenchmarkCategory("Notifications"), Benchmark]
    public ValueTask<int> Kevlar_YieldingAsyncNotification() =>
        KevlarYieldingAsyncNotification.ExecuteAsync(static _ => throw PrimaryError);
}
