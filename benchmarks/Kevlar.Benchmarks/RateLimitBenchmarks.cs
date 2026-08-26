using System.Threading.RateLimiting;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Kevlar.Extensions.RateLimiting;
using Polly;

namespace Kevlar.Benchmarks;

/// <summary>
/// Rate limit uncontended path: the permit budget is effectively unlimited, so every
/// acquisition succeeds. Measures per-call bookkeeping of the limiter.
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

    [BenchmarkCategory("TokenBucketUncontended"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_TokenBucketUncontended() =>
        KevlarRateLimit.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("TokenBucketUncontended"), Benchmark]
    public ValueTask<int> Polly_TokenBucketUncontended() =>
        PollyRateLimit.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("TokenBucketUncontended"), Benchmark]
    public ValueTask<int> Kevlar_WithHooks_Uncontended() =>
        KevlarRateLimitWithHooks.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Uncontended"), Benchmark]
    public ValueTask<int> Kevlar_FrameworkAdapter_Uncontended() =>
        KevlarFrameworkRateLimit.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Uncontended"), Benchmark]
    public ValueTask<int> Kevlar_PartitionedFrameworkAdapter_Uncontended() =>
        KevlarPartitionedFrameworkRateLimit.ExecuteAsync(static _ => new ValueTask<int>(42));
}
