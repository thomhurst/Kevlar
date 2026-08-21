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
    private static readonly Shield<int> FixedLaunch = Shield.For<int>().Hedge(2, TimeSpan.Zero);
    private static readonly Shield<int> SyncHookLaunch = Shield.For<int>().Hedge(options =>
    {
        options.MaxAttempts = 2;
        options.Delay = TimeSpan.Zero;
        options.OnHedge = static _ => { };
    });
    private static readonly Shield<int> CompletedAsyncHookLaunch = Shield.For<int>().Hedge(options =>
    {
        options.MaxAttempts = 2;
        options.Delay = TimeSpan.Zero;
        options.OnHedgeAsync = static _ => ValueTask.CompletedTask;
    });
    private static readonly Shield<int> YieldingAsyncHookLaunch = Shield.For<int>().Hedge(options =>
    {
        options.MaxAttempts = 2;
        options.Delay = TimeSpan.Zero;
        options.OnHedgeAsync = static async _ => await Task.Yield();
    });
    private static readonly Shield<int> GeneratedLaunch = Shield.For<int>().Hedge(options =>
    {
        options.MaxAttempts = 2;
        options.Delay = TimeSpan.Zero;
        options.ActionGenerator = HedgeActionGenerator.Create<int>(
            static hedge => hedge.OriginalAction);
    });

    private static readonly ResiliencePipeline<int> PollyHedge = new ResiliencePipelineBuilder<int>()
        .AddHedging(new HedgingStrategyOptions<int> { MaxHedgedAttempts = 1, Delay = TimeSpan.FromSeconds(10) })
        .Build();

    private int _attempt;

    [BenchmarkCategory("PrimaryWins"), Benchmark(Baseline = true)]
    public ValueTask<int> KevlarPrimaryWins() => KevlarHedge.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("PrimaryWins"), Benchmark]
    public ValueTask<int> PollyPrimaryWins() => PollyHedge.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("HedgeCallbacks"), Benchmark(Baseline = true)]
    public ValueTask<int> FixedHedge() => ExecuteFailureThenSuccess(FixedLaunch);

    [BenchmarkCategory("HedgeCallbacks"), Benchmark]
    public ValueTask<int> SyncHook() => ExecuteFailureThenSuccess(SyncHookLaunch);

    [BenchmarkCategory("HedgeCallbacks"), Benchmark]
    public ValueTask<int> CompletedAsyncHook() => ExecuteFailureThenSuccess(CompletedAsyncHookLaunch);

    [BenchmarkCategory("HedgeCallbacks"), Benchmark]
    public ValueTask<int> YieldingAsyncHook() => ExecuteFailureThenSuccess(YieldingAsyncHookLaunch);

    [BenchmarkCategory("HedgeCallbacks"), Benchmark]
    public ValueTask<int> GeneratedAction() => ExecuteFailureThenSuccess(GeneratedLaunch);

    private ValueTask<int> ExecuteFailureThenSuccess(Shield<int> shield)
    {
        Volatile.Write(ref _attempt, 0);
        return shield.ExecuteAsync(this, static (benchmark, _) =>
            Interlocked.Increment(ref benchmark._attempt) == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(42));
    }
}
