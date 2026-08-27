using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;
using Polly.Retry;

namespace Kevlar.Benchmarks;

/// <summary>
/// Typed pipelines with result-based handling: retry configured to treat a sentinel
/// result value as a failure. The happy path returns a non-matching value, so this
/// measures the cost of judging each result without ever retrying.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class TypedResultBenchmarks
{
    private static readonly Shield<int> KevlarResultRetry = Shield.For<int>()
        .WhenResultEquals(-1)
        .Retry(3, Backoff.None);

    private static readonly ResiliencePipeline<int> PollyResultRetry = new ResiliencePipelineBuilder<int>()
        .AddRetry(new RetryStrategyOptions<int>
        {
            ShouldHandle = new PredicateBuilder<int>().HandleResult(-1),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.Zero,
        })
        .Build();

    [BenchmarkCategory("ResultJudged"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_ResultJudged() => KevlarResultRetry.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("ResultJudged"), Benchmark]
    public ValueTask<int> Polly_ResultJudged() => PollyResultRetry.ExecuteAsync(static _ => new ValueTask<int>(42));
}
