using System.Threading.RateLimiting;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Kevlar.Benchmarks;

/// <summary>
/// Composed pipelines on the happy path: the classic Timeout → Retry → CircuitBreaker
/// trio, and a deep five-strategy chain. Measures how per-call overhead scales with
/// pipeline depth when nothing goes wrong.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PipelineBenchmarks
{
    private const double FailureRatio = 0.1;
    private const int MinimumThroughput = 100;
    private static readonly TimeSpan SamplingWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(5);

    private static readonly Shield KevlarTrio = Shield
        .Timeout(TimeSpan.FromSeconds(10))
        .Retry(3, Backoff.None)
        .CircuitBreaker(ConfigureKevlarRatioBreaker);

    private static readonly Shield KevlarDeep = Shield
        .RateLimit(1_000_000_000, TimeSpan.FromSeconds(1))
        .Timeout(TimeSpan.FromSeconds(10))
        .Retry(3, Backoff.None)
        .CircuitBreaker(ConfigureKevlarRatioBreaker)
        .ConcurrencyLimit(1024);

    private static readonly ResiliencePipeline PollyTrio = new ResiliencePipelineBuilder()
        .AddTimeout(new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(10) })
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3, Delay = TimeSpan.Zero })
        .AddCircuitBreaker(CreatePollyRatioBreakerOptions())
        .Build();

    private static readonly ResiliencePipeline PollyDeep = new ResiliencePipelineBuilder()
        .AddRateLimiter(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1_000_000_000,
            TokensPerPeriod = 1_000_000_000,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            AutoReplenishment = true,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        }))
        .AddTimeout(new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(10) })
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3, Delay = TimeSpan.Zero })
        .AddCircuitBreaker(CreatePollyRatioBreakerOptions())
        .AddConcurrencyLimiter(1024)
        .Build();

    private static void ConfigureKevlarRatioBreaker(CircuitBreakerOptions options)
    {
        options.FailureRatio = FailureRatio;
        options.MinimumThroughput = MinimumThroughput;
        options.SamplingWindow = SamplingWindow;
        options.BreakDuration = BreakDuration;
    }

    private static CircuitBreakerStrategyOptions CreatePollyRatioBreakerOptions() => new()
    {
        FailureRatio = FailureRatio,
        MinimumThroughput = MinimumThroughput,
        SamplingDuration = SamplingWindow,
        BreakDuration = BreakDuration,
    };

    [BenchmarkCategory("RatioTimeoutRetryBreaker"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_RatioTimeoutRetryBreaker() =>
        KevlarTrio.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("RatioTimeoutRetryBreaker"), Benchmark]
    public ValueTask<int> Polly_RatioTimeoutRetryBreaker() =>
        PollyTrio.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("TokenBucketRatioFiveStrategyChain"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_TokenBucketRatioFiveStrategyChain() =>
        KevlarDeep.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("TokenBucketRatioFiveStrategyChain"), Benchmark]
    public ValueTask<int> Polly_TokenBucketRatioFiveStrategyChain() =>
        PollyDeep.ExecuteAsync(static _ => new ValueTask<int>(42));
}
