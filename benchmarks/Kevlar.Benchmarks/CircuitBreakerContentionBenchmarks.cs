using BenchmarkDotNet.Attributes;
using Polly;
using Polly.CircuitBreaker;

namespace Kevlar.Benchmarks;

/// <summary>
/// Fast-fail rejection while several workers share one open circuit — the outage case, where
/// every caller is refused and any shared lock on the rejection path becomes the bottleneck.
/// Rejections are observed as outcomes, so exception throwing does not mask the contention.
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class CircuitBreakerContentionBenchmarks
{
    private const int Workers = 8;
    private const int CallsPerWorker = 1_024;
    private const int Operations = Workers * CallsPerWorker;

    private static readonly Shield KevlarOpenBreaker =
        Shield.CircuitBreaker(consecutiveFailures: 1, breakDuration: TimeSpan.FromDays(1));

    private static readonly ResiliencePipeline PollyOpenBreaker = new ResiliencePipelineBuilder()
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 2,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromDays(1),
        })
        .Build();

    [GlobalSetup]
    public void Setup()
    {
        _ = KevlarOpenBreaker.ExecuteOutcome<int>(static _ => throw new InvalidOperationException());
        for (var failure = 0; failure < 2; failure++)
        {
            _ = ExecutePolly(static () => Polly.Outcome.FromException<int>(new InvalidOperationException()));
        }

        if (!Kevlar_ContendedOpenRejection() || !Polly_ContendedOpenRejection())
        {
            throw new InvalidOperationException("Both breakers must be open before measurement.");
        }
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Operations)]
    public bool Kevlar_ContendedOpenRejection()
    {
        var rejectedAll = true;
        Parallel.For(0, Workers, _ =>
        {
            for (var call = 0; call < CallsPerWorker; call++)
            {
                var outcome = KevlarOpenBreaker
                    .ExecuteOutcomeAsync(static _ => new ValueTask<int>(42))
                    .GetAwaiter()
                    .GetResult();
                if (outcome.Exception is not CircuitOpenException)
                {
                    rejectedAll = false;
                }
            }
        });

        return rejectedAll;
    }

    [Benchmark(OperationsPerInvoke = Operations)]
    public bool Polly_ContendedOpenRejection()
    {
        var rejectedAll = true;
        Parallel.For(0, Workers, _ =>
        {
            for (var call = 0; call < CallsPerWorker; call++)
            {
                var outcome = ExecutePolly(static () => Polly.Outcome.FromResult(42));
                if (outcome.Exception is not BrokenCircuitException)
                {
                    rejectedAll = false;
                }
            }
        });

        return rejectedAll;
    }

    private static Polly.Outcome<int> ExecutePolly(Func<Polly.Outcome<int>> callback)
    {
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            return PollyOpenBreaker
                .ExecuteOutcomeAsync(
                    static (_, callback) => new ValueTask<Polly.Outcome<int>>(callback()),
                    context,
                    callback)
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
