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
    private static readonly Shield KevlarTrio = Shield
        .Timeout(TimeSpan.FromSeconds(10))
        .Retry(3)
        .CircuitBreaker(5, TimeSpan.FromSeconds(30));

    private static readonly Shield KevlarDeep = Shield
        .RateLimit(1_000_000_000, TimeSpan.FromSeconds(1))
        .Timeout(TimeSpan.FromSeconds(10))
        .Retry(3)
        .CircuitBreaker(5, TimeSpan.FromSeconds(30))
        .ConcurrencyLimit(1024);

    private static readonly ResiliencePipeline PollyTrio = new ResiliencePipelineBuilder()
        .AddTimeout(new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(10) })
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions())
        .Build();

    private static readonly ResiliencePipeline PollyDeep = new ResiliencePipelineBuilder()
        .AddRateLimiter(new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1_000_000_000,
            Window = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
        }))
        .AddTimeout(new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(10) })
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions())
        .AddConcurrencyLimiter(1024)
        .Build();

    [BenchmarkCategory("TimeoutRetryBreaker"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_TimeoutRetryBreaker() => KevlarTrio.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("TimeoutRetryBreaker"), Benchmark]
    public ValueTask<int> Polly_TimeoutRetryBreaker() => PollyTrio.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("FiveStrategyChain"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_FiveStrategyChain() => KevlarDeep.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("FiveStrategyChain"), Benchmark]
    public ValueTask<int> Polly_FiveStrategyChain() => PollyDeep.ExecuteAsync(static _ => new ValueTask<int>(42));
}
