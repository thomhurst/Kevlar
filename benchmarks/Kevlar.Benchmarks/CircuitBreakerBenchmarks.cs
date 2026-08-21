using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;
using Polly.CircuitBreaker;

namespace Kevlar.Benchmarks;

/// <summary>
/// Circuit breaker: the closed happy path (success bookkeeping per call) and the open
/// fast-fail path (rejection cost while the circuit is broken, exception included).
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CircuitBreakerBenchmarks
{
    private static readonly Shield KevlarBreaker = Shield.CircuitBreaker(5, TimeSpan.FromSeconds(30));
    private static readonly Shield KevlarDynamicDurationBreaker = Shield.CircuitBreaker(options =>
    {
        options.ConsecutiveFailures = 5;
        options.BreakDurationGenerator = static _ => new ValueTask<TimeSpan>(TimeSpan.FromSeconds(30));
    });
    private static readonly Shield KevlarAsyncCallbackBreaker = Shield.CircuitBreaker(options =>
    {
        options.ConsecutiveFailures = 5;
        options.OnStateChangedAsync = static _ => default;
    });
    private static readonly Shield KevlarOpenBreaker = Shield.CircuitBreaker(1, TimeSpan.FromDays(1));

    private static readonly ResiliencePipeline PollyBreaker = new ResiliencePipelineBuilder()
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions())
        .Build();

    private static readonly CircuitBreakerManualControl PollyManualControl = new();
    private static readonly ResiliencePipeline PollyOpenBreaker = new ResiliencePipelineBuilder()
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions { ManualControl = PollyManualControl })
        .Build();

    [GlobalSetup(Target = nameof(Kevlar_OpenFastFail))]
    public async Task TripKevlarBreaker()
    {
        try
        {
            await KevlarOpenBreaker.ExecuteAsync(static _ => throw new InvalidOperationException("trip"));
        }
        catch (InvalidOperationException)
        {
        }
    }

    [GlobalSetup(Target = nameof(Polly_OpenFastFail))]
    public Task IsolatePollyBreaker() => PollyManualControl.IsolateAsync();

    [BenchmarkCategory("ClosedHappyPath"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_ClosedHappyPath() => KevlarBreaker.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("ClosedHappyPath"), Benchmark]
    public ValueTask<int> Polly_ClosedHappyPath() => PollyBreaker.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("ClosedHappyPath"), Benchmark]
    public ValueTask<int> Kevlar_DynamicDurationConfigured() =>
        KevlarDynamicDurationBreaker.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("ClosedHappyPath"), Benchmark]
    public ValueTask<int> Kevlar_AsyncCallbackConfigured() =>
        KevlarAsyncCallbackBreaker.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("OpenFastFail"), Benchmark(Baseline = true)]
    public async ValueTask<bool> Kevlar_OpenFastFail()
    {
        try
        {
            await KevlarOpenBreaker.ExecuteAsync(static _ => new ValueTask<int>(42));
            return false;
        }
        catch (CircuitOpenException)
        {
            return true;
        }
    }

    [BenchmarkCategory("OpenFastFail"), Benchmark]
    public async ValueTask<bool> Polly_OpenFastFail()
    {
        try
        {
            await PollyOpenBreaker.ExecuteAsync(static _ => new ValueTask<int>(42));
            return false;
        }
        catch (BrokenCircuitException)
        {
            return true;
        }
    }
}
