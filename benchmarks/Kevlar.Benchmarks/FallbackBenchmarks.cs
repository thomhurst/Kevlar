using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;
using Polly.Fallback;

namespace Kevlar.Benchmarks;

/// <summary>
/// Fallback strategy: the pass-through path (execution succeeds, fallback unused) and
/// the triggered path (execution throws, fallback value substituted).
/// Polly only offers fallback on typed pipelines, so comparative groups use typed pipelines.
/// Kevlar's void pass-through is measured against an empty void shield.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FallbackBenchmarks
{
    private static readonly InvalidOperationException PrimaryError = new("primary failed");

    private static readonly Shield<int> KevlarFallback = Shield.For<int>()
        .When<InvalidOperationException>()
        .FallbackTo(7);

    private static readonly Shield KevlarVoidFallback = Shield.Fallback(static _ => default);

    private static readonly Shield<int> KevlarCompletedAsyncNotification = Shield.For<int>()
        .When<InvalidOperationException>()
        .FallbackTo(
            7,
            static options => options.OnFallback = static _ => ValueTask.CompletedTask);

    private static readonly Shield<int> KevlarYieldingAsyncNotification = Shield.For<int>()
        .When<InvalidOperationException>()
        .FallbackTo(
            7,
            static options => options.OnFallback = static async _ => await Task.Yield());

    private static readonly Shield<int> KevlarSynchronousDelegate = Shield.For<int>()
        .When<InvalidOperationException>()
        .Fallback(static _ => new ValueTask<int>(7));

    private static readonly ResiliencePipeline<int> PollyFallback = new ResiliencePipelineBuilder<int>()
        .AddFallback(new FallbackStrategyOptions<int>
        {
            ShouldHandle = new PredicateBuilder<int>().Handle<InvalidOperationException>(),
            FallbackAction = static _ => Polly.Outcome.FromResultAsValueTask(7),
        })
        .Build();

    [BenchmarkCategory("PassThrough"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_PassThrough() => KevlarFallback.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("PassThrough"), Benchmark]
    public ValueTask<int> Polly_PassThrough() => PollyFallback.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("VoidPassThrough"), Benchmark(Baseline = true)]
    public ValueTask Kevlar_EmptyVoid() => Shield.Empty.ExecuteAsync(static _ => ValueTask.CompletedTask);

    [BenchmarkCategory("VoidPassThrough"), Benchmark]
    public ValueTask Kevlar_VoidPassThrough() =>
        KevlarVoidFallback.ExecuteAsync(static _ => ValueTask.CompletedTask);

    [BenchmarkCategory("Triggered"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_Triggered() => KevlarFallback.ExecuteAsync(static _ => throw PrimaryError);

    [BenchmarkCategory("SynchronousTriggered"), Benchmark]
    public int Kevlar_SynchronousDelegate_Triggered() =>
        KevlarSynchronousDelegate.Execute(static _ => throw PrimaryError);

    [BenchmarkCategory("Triggered"), Benchmark]
    public ValueTask<int> Polly_Triggered() => PollyFallback.ExecuteAsync(static ValueTask<int> (_) => throw PrimaryError);

    [BenchmarkCategory("Notifications"), Benchmark(
        Baseline = true,
        Description = "Kevlar fallback without notification")]
    public ValueTask<int> Kevlar_NoNotification() => KevlarFallback.ExecuteAsync(static _ => throw PrimaryError);

    [BenchmarkCategory("Notifications"), Benchmark]
    public ValueTask<int> Kevlar_CompletedAsyncNotification() =>
        KevlarCompletedAsyncNotification.ExecuteAsync(static _ => throw PrimaryError);

    [BenchmarkCategory("Notifications"), Benchmark]
    public ValueTask<int> Kevlar_YieldingAsyncNotification() =>
        KevlarYieldingAsyncNotification.ExecuteAsync(static _ => throw PrimaryError);
}
