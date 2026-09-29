using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;
using Polly.RateLimiting;

namespace Kevlar.Benchmarks;

/// <summary>
/// Concurrency limit uncontended path: a single caller against a large permit count, so
/// no queueing ever happens. Measures the acquire/release cost per call. The
/// <c>RejectedFastFail</c> scenarios hold the only permit (no queue) and measure the rejection
/// cost, caught as an exception or read from the no-throw outcome API.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ConcurrencyLimitBenchmarks
{
    private static readonly Shield KevlarConcurrency = Shield.ConcurrencyLimit(1024);
    private static readonly Shield KevlarAdaptiveConcurrency = Shield.ConcurrencyLimit(new AdaptiveConcurrencyLimitOptions
    {
        InitialLimit = 1024,
        MaxLimit = 1024,
    });
    private static readonly Shield KevlarConcurrencyWithHooks = Shield.ConcurrencyLimit(options =>
    {
        options.MaxConcurrency = 1024;
        options.OnRejected = static _ => ValueTask.CompletedTask;
    });

    private static readonly ResiliencePipeline PollyConcurrency = new ResiliencePipelineBuilder()
        .AddConcurrencyLimiter(1024)
        .Build();

    private static readonly Shield KevlarSaturated = Shield.ConcurrencyLimit(1);
    private static readonly ResiliencePipeline PollySaturated = new ResiliencePipelineBuilder()
        .AddConcurrencyLimiter(permitLimit: 1, queueLimit: 0)
        .Build();

    // Never completed: the single permit stays held for the whole benchmark process.
    private static readonly TaskCompletionSource<int> HoldPermit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<int>? _kevlarHolder;
    private static Task<int>? _pollyHolder;

    [GlobalSetup(Targets = [nameof(Kevlar_RejectedFastFail), nameof(Kevlar_RejectedFastFailOutcome)])]
    public void SaturateKevlar() =>
        _kevlarHolder ??= KevlarSaturated.ExecuteAsync(static _ => new ValueTask<int>(HoldPermit.Task)).AsTask();

    [GlobalSetup(Targets = [nameof(Polly_RejectedFastFail), nameof(Polly_RejectedFastFailOutcome)])]
    public void SaturatePolly() =>
        _pollyHolder ??= PollySaturated.ExecuteAsync(static _ => new ValueTask<int>(HoldPermit.Task)).AsTask();

    [BenchmarkCategory("Uncontended"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_Uncontended() => KevlarConcurrency.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Uncontended"), Benchmark]
    public ValueTask<int> Polly_Uncontended() => PollyConcurrency.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Uncontended"), Benchmark(Description = "Kevlar concurrency limiter with hooks")]
    public ValueTask<int> Kevlar_WithHooks_Uncontended() =>
        KevlarConcurrencyWithHooks.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Uncontended"), Benchmark]
    public ValueTask<int> Kevlar_Adaptive_Uncontended() =>
        KevlarAdaptiveConcurrency.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("RejectedFastFail"), Benchmark(Baseline = true)]
    public async ValueTask<bool> Kevlar_RejectedFastFail()
    {
        try
        {
            await KevlarSaturated.ExecuteAsync(static _ => new ValueTask<int>(42));
            return false;
        }
        catch (ConcurrencyLimitExceededException)
        {
            return true;
        }
    }

    [BenchmarkCategory("RejectedFastFail"), Benchmark]
    public async ValueTask<bool> Polly_RejectedFastFail()
    {
        try
        {
            await PollySaturated.ExecuteAsync(static _ => new ValueTask<int>(42));
            return false;
        }
        catch (RateLimiterRejectedException)
        {
            return true;
        }
    }

    [BenchmarkCategory("RejectedFastFail"), Benchmark]
    public async ValueTask<bool> Kevlar_RejectedFastFailOutcome()
    {
        var outcome = await KevlarSaturated.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));
        return outcome.Exception is ConcurrencyLimitExceededException;
    }

    [BenchmarkCategory("RejectedFastFail"), Benchmark]
    public async ValueTask<bool> Polly_RejectedFastFailOutcome()
    {
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            var outcome = await PollySaturated.ExecuteOutcomeAsync(
                static (_, _) => Polly.Outcome.FromResultAsValueTask(42),
                context,
                state: 0);
            return outcome.Exception is RateLimiterRejectedException;
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
