using System.Threading.RateLimiting;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
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
        options.OnRejected = static _ => { };
        options.OnRejectedAsync = static _ => ValueTask.CompletedTask;
    });

    private static readonly ResiliencePipeline PollyRateLimit = new ResiliencePipelineBuilder()
        .AddRateLimiter(new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1_000_000_000,
            Window = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
        }))
        .Build();

    [BenchmarkCategory("Uncontended"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_Uncontended() => KevlarRateLimit.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Uncontended"), Benchmark]
    public ValueTask<int> Polly_Uncontended() => PollyRateLimit.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Uncontended"), Benchmark]
    public ValueTask<int> Kevlar_WithHooks_Uncontended() =>
        KevlarRateLimitWithHooks.ExecuteAsync(static _ => new ValueTask<int>(42));
}
