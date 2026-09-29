using System.Threading.RateLimiting;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Kevlar.Extensions.RateLimiting;
using Polly;
using Polly.RateLimiting;

namespace Kevlar.Benchmarks;

/// <summary>
/// Rate limit uncontended path: the permit budget is effectively unlimited, so every
/// acquisition succeeds. Measures per-call bookkeeping of the limiter. The
/// <c>RejectedFastFail</c> scenarios exhaust a one-permit, one-hour budget up front and measure
/// the rejection cost, caught as an exception or read from the no-throw outcome API.
/// Polly delegates to System.Threading.RateLimiting via Polly.RateLimiting.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class RateLimitBenchmarks
{
    private static readonly Shield KevlarRateLimit = Shield.RateLimit(1_000_000_000, TimeSpan.FromSeconds(1));
    private static readonly Shield KevlarRateLimitWithHooks = Shield.RateLimit(options =>
    {
        options.Permits = 1_000_000_000;
        options.Window = TimeSpan.FromSeconds(1);
        options.OnRejected = static _ => ValueTask.CompletedTask;
    });
    private static readonly FixedWindowRateLimiter FrameworkLimiter = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = 1_000_000_000,
        Window = TimeSpan.FromSeconds(1),
        QueueLimit = 0,
    });
    private static readonly Shield KevlarFrameworkRateLimit = Shield.Empty.UseRateLimiter(FrameworkLimiter);
    private static readonly PartitionedRateLimiter<KevlarContext> FrameworkPartitionedLimiter =
        PartitionedRateLimiter.Create<KevlarContext, int>(context =>
            RateLimitPartition.Get(
                context.IsSynchronous ? 0 : 1,
                static _ => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1_000_000_000,
                    Window = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                })));
    private static readonly Shield KevlarPartitionedFrameworkRateLimit =
        Shield.Empty.UseRateLimiter(FrameworkPartitionedLimiter);

    private static readonly ResiliencePipeline PollyRateLimit = new ResiliencePipelineBuilder()
        .AddRateLimiter(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1_000_000_000,
            TokensPerPeriod = 1_000_000_000,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            AutoReplenishment = true,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        }))
        .Build();

    private static readonly Shield KevlarExhausted = Shield.RateLimit(1, perWindow: TimeSpan.FromHours(1));
    private static readonly ResiliencePipeline PollyExhausted = new ResiliencePipelineBuilder()
        .AddRateLimiter(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromHours(1),
            AutoReplenishment = false,
            QueueLimit = 0,
        }))
        .Build();

    // Setup may run more than once per process (for example, in-process jobs); an already
    // exhausted budget is fine, so both consume the single permit without throwing.
    [GlobalSetup(Targets = [nameof(Kevlar_RejectedFastFail), nameof(Kevlar_RejectedFastFailOutcome)])]
    public async Task ExhaustKevlar() =>
        await KevlarExhausted.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));

    [GlobalSetup(Targets = [nameof(Polly_RejectedFastFail), nameof(Polly_RejectedFastFailOutcome)])]
    public async Task ExhaustPolly()
    {
        var context = ResilienceContextPool.Shared.Get();
        await PollyExhausted.ExecuteOutcomeAsync(
            static (_, _) => Polly.Outcome.FromResultAsValueTask(42),
            context,
            state: 0);
        ResilienceContextPool.Shared.Return(context);
    }

    [BenchmarkCategory("TokenBucketUncontended"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_TokenBucketUncontended() =>
        KevlarRateLimit.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("TokenBucketUncontended"), Benchmark]
    public ValueTask<int> Polly_TokenBucketUncontended() =>
        PollyRateLimit.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("TokenBucketUncontended"), Benchmark(Description = "Kevlar token bucket with hooks")]
    public ValueTask<int> Kevlar_WithHooks_Uncontended() =>
        KevlarRateLimitWithHooks.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Uncontended"), Benchmark]
    public ValueTask<int> Kevlar_FrameworkAdapter_Uncontended() =>
        KevlarFrameworkRateLimit.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Uncontended"), Benchmark]
    public ValueTask<int> Kevlar_PartitionedFrameworkAdapter_Uncontended() =>
        KevlarPartitionedFrameworkRateLimit.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("RejectedFastFail"), Benchmark(Baseline = true)]
    public async ValueTask<bool> Kevlar_RejectedFastFail()
    {
        try
        {
            await KevlarExhausted.ExecuteAsync(static _ => new ValueTask<int>(42));
            return false;
        }
        catch (RateLimitExceededException)
        {
            return true;
        }
    }

    [BenchmarkCategory("RejectedFastFail"), Benchmark]
    public async ValueTask<bool> Polly_RejectedFastFail()
    {
        try
        {
            await PollyExhausted.ExecuteAsync(static _ => new ValueTask<int>(42));
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
        var outcome = await KevlarExhausted.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));
        return outcome.Exception is RateLimitExceededException;
    }

    [BenchmarkCategory("RejectedFastFail"), Benchmark]
    public async ValueTask<bool> Polly_RejectedFastFailOutcome()
    {
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            var outcome = await PollyExhausted.ExecuteOutcomeAsync(
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
