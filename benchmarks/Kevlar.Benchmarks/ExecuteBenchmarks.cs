using BenchmarkDotNet.Attributes;
using Kevlar;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using KevlarPolicy = Kevlar.Policy;

namespace Kevlar.Benchmarks;

/// <summary>
/// Happy-path overhead comparison: what each library costs per execution when nothing goes wrong.
/// Run with: dotnet run -c Release --project benchmarks/Kevlar.Benchmarks -- --filter '*'
/// </summary>
[MemoryDiagnoser]
public class ExecuteBenchmarks
{
    private static readonly KevlarPolicy KevlarEmpty = KevlarPolicy.Empty;
    private static readonly KevlarPolicy KevlarRetry = KevlarPolicy.Retry(3);
    private static readonly KevlarPolicy KevlarPipeline = KevlarPolicy
        .Timeout(TimeSpan.FromSeconds(10))
        .Retry(3)
        .CircuitBreaker(5, TimeSpan.FromSeconds(30));

    private static readonly ResiliencePipeline PollyEmpty = ResiliencePipeline.Empty;
    private static readonly ResiliencePipeline PollyRetry = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
        .Build();
    private static readonly ResiliencePipeline PollyPipeline = new ResiliencePipelineBuilder()
        .AddTimeout(new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(10) })
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions())
        .Build();

    [Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_Empty() => KevlarEmpty.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> Polly_Empty() => PollyEmpty.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> Kevlar_Retry() => KevlarRetry.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> Polly_Retry() => PollyRetry.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> Kevlar_TimeoutRetryBreaker() => KevlarPipeline.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> Polly_TimeoutRetryBreaker() => PollyPipeline.ExecuteAsync(static _ => new ValueTask<int>(42));
}
