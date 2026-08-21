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
    private const double FailureRatio = 0.1;
    private const int MinimumThroughput = 100;
    private static readonly TimeSpan SamplingWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(5);

    private static readonly Shield KevlarBreaker = Shield.CircuitBreaker(ConfigureKevlarRatioBreaker);
    private static readonly Shield KevlarDynamicDurationBreaker = Shield.CircuitBreaker(options =>
    {
        ConfigureKevlarRatioBreaker(options);
        options.BreakDurationGenerator = static _ => new ValueTask<TimeSpan>(BreakDuration);
    });
    private static readonly Shield KevlarAsyncCallbackBreaker = Shield.CircuitBreaker(options =>
    {
        ConfigureKevlarRatioBreaker(options);
        options.OnStateChangedAsync = static _ => default;
    });

    private static readonly CircuitBreakerMonitor KevlarManualControl = new();
    private static readonly Shield KevlarOpenBreaker = Shield.CircuitBreaker(options =>
    {
        ConfigureKevlarRatioBreaker(options);
        options.Monitor = KevlarManualControl;
    });

    private static readonly ResiliencePipeline PollyBreaker = new ResiliencePipelineBuilder()
        .AddCircuitBreaker(CreatePollyRatioBreakerOptions())
        .Build();

    private static readonly CircuitBreakerManualControl PollyManualControl = new();
    private static readonly ResiliencePipeline PollyOpenBreaker = new ResiliencePipelineBuilder()
        .AddCircuitBreaker(CreatePollyRatioBreakerOptions(PollyManualControl))
        .Build();

    [GlobalSetup(Target = nameof(Kevlar_IsolatedFastFail))]
    public ValueTask IsolateKevlarBreaker() => KevlarManualControl.IsolateAsync();

    [GlobalSetup(Target = nameof(Polly_IsolatedFastFail))]
    public Task IsolatePollyBreaker() => PollyManualControl.IsolateAsync();

    private static void ConfigureKevlarRatioBreaker(CircuitBreakerOptions options)
    {
        options.FailureRatio = FailureRatio;
        options.MinimumThroughput = MinimumThroughput;
        options.SamplingWindow = SamplingWindow;
        options.BreakDuration = BreakDuration;
    }

    private static CircuitBreakerStrategyOptions CreatePollyRatioBreakerOptions(
        CircuitBreakerManualControl? manualControl = null) => new()
        {
            FailureRatio = FailureRatio,
            MinimumThroughput = MinimumThroughput,
            SamplingDuration = SamplingWindow,
            BreakDuration = BreakDuration,
            ManualControl = manualControl,
        };

    [BenchmarkCategory("RatioClosedHappyPath"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_RatioClosedHappyPath() =>
        KevlarBreaker.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("RatioClosedHappyPath"), Benchmark]
    public ValueTask<int> Polly_RatioClosedHappyPath() =>
        PollyBreaker.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("RatioClosedHappyPath"), Benchmark]
    public ValueTask<int> Kevlar_DynamicDurationConfigured() =>
        KevlarDynamicDurationBreaker.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("RatioClosedHappyPath"), Benchmark]
    public ValueTask<int> Kevlar_AsyncCallbackConfigured() =>
        KevlarAsyncCallbackBreaker.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("IsolatedFastFail"), Benchmark(Baseline = true)]
    public async ValueTask<bool> Kevlar_IsolatedFastFail()
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

    [BenchmarkCategory("IsolatedFastFail"), Benchmark]
    public async ValueTask<bool> Polly_IsolatedFastFail()
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
