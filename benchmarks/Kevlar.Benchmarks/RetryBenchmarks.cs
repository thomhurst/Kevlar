using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;
using Polly.Retry;

namespace Kevlar.Benchmarks;

/// <summary>
/// Retry strategy: the happy path (no failures, judge overhead only) and the recovery
/// path (two failures then success per execution, zero backoff so only strategy
/// machinery is measured, not sleeps).
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class RetryBenchmarks
{
    private static readonly InvalidOperationException RecoverableError = new("transient");

    private static readonly Shield KevlarRetry = Shield.Retry(3);
    private static readonly Shield KevlarRetryNoBackoff = Shield.Retry(options =>
    {
        options.MaxRetries = 3;
        options.Backoff = Backoff.None;
    });

    private static readonly ResiliencePipeline PollyRetry = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
        .Build();
    private static readonly ResiliencePipeline PollyRetryNoBackoff = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3, Delay = TimeSpan.Zero })
        .Build();

    private sealed class Counter { public int Value; }

    private readonly Counter _kevlarCounter = new();
    private readonly Counter _pollyCounter = new();

    [BenchmarkCategory("HappyPath"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_HappyPath() => KevlarRetry.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("HappyPath"), Benchmark]
    public ValueTask<int> Polly_HappyPath() => PollyRetry.ExecuteAsync(static _ => new ValueTask<int>(42));

    // Each execution fails twice then succeeds: the counter cycles 1,2,3 across the
    // attempts of one call, so every invocation costs exactly two thrown exceptions.
    [BenchmarkCategory("Recovery"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_Recovery() => KevlarRetryNoBackoff.ExecuteAsync(_kevlarCounter, static (c, _) =>
    {
        if (++c.Value % 3 != 0)
        {
            throw RecoverableError;
        }

        return new ValueTask<int>(42);
    });

    [BenchmarkCategory("Recovery"), Benchmark]
    public ValueTask<int> Polly_Recovery() => PollyRetryNoBackoff.ExecuteAsync(static (c, _) =>
    {
        if (++c.Value % 3 != 0)
        {
            throw RecoverableError;
        }

        return new ValueTask<int>(42);
    }, _pollyCounter);
}
