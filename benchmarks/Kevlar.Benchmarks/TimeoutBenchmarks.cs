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

    private static readonly Shield KevlarGeneratedTimeout = Shield.Timeout(options =>
        options.TimeoutGenerator = static _ =>
            new ValueTask<TimeSpan>(TimeSpan.FromSeconds(10)));

    private static readonly Shield KevlarAsyncGeneratedTimeout = Shield.Timeout(options =>
        options.TimeoutGenerator = GenerateTimeoutAsync);

    private static readonly Shield KevlarAsyncHookConfigured = Shield.Timeout(options =>
    {
        options.Timeout = TimeSpan.FromSeconds(10);
        options.OnTimeout = static _ => ValueTask.CompletedTask;
    });

    private static readonly ResiliencePipeline PollyTimeout = new ResiliencePipelineBuilder()
        .AddTimeout(new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(10) })
        .Build();

    [BenchmarkCategory("HappyPath"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_HappyPath() => KevlarTimeout.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("HappyPath"), Benchmark]
    public ValueTask<int> Polly_HappyPath() => PollyTimeout.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("HappyPath"), Benchmark]
    public ValueTask<int> Kevlar_SynchronousGenerator_HappyPath() =>
        KevlarGeneratedTimeout.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("HappyPath"), Benchmark]
    public ValueTask<int> Kevlar_AsynchronousGenerator_HappyPath() =>
        KevlarAsyncGeneratedTimeout.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("HappyPath"), Benchmark]
    public ValueTask<int> Kevlar_AsyncHookConfigured_HappyPath() =>
        KevlarAsyncHookConfigured.ExecuteAsync(static _ => new ValueTask<int>(42));

    private static async ValueTask<TimeSpan> GenerateTimeoutAsync(KevlarContext _)
    {
        await Task.Yield();
        return TimeSpan.FromSeconds(10);
    }
}
