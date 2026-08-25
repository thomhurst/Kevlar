using System.Diagnostics.Metrics;
using BenchmarkDotNet.Attributes;

namespace Kevlar.Benchmarks;

/// <summary>
/// Telemetry overhead on representative synchronously-completing happy paths. The disabled
/// parameter measures the normal no-listener path; the enabled parameter subscribes every
/// instrument through a minimal <see cref="MeterListener"/>.
/// </summary>
[MemoryDiagnoser]
public class TelemetryBenchmarks
{
    private static readonly Shield Empty = Shield.Empty.WithName("benchmark");
    private static readonly Shield Retry = Shield.Retry(0, Backoff.None).WithName("benchmark");
    private static readonly Shield CircuitBreaker = Shield.CircuitBreaker(5, TimeSpan.FromMinutes(1)).WithName("benchmark");
    private static readonly Shield RateLimit = Shield.RateLimit(1_000_000_000, TimeSpan.FromSeconds(1)).WithName("benchmark");
    private static readonly Shield ConcurrencyLimit = Shield.ConcurrencyLimit(1024).WithName("benchmark");

    private MeterListener? _listener;

    [Params(false, true)]
    public bool ListenerEnabled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (!ListenerEnabled)
        {
            return;
        }

        _listener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == KevlarDiagnostics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<long>(static (_, _, _, _) => { });
        _listener.SetMeasurementEventCallback<double>(static (_, _, _, _) => { });
        _listener.Start();
    }

    [GlobalCleanup]
    public void Cleanup() => _listener?.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask<int> EmptyShield() => Empty.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> RetryHappyPath() => Retry.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> CircuitBreakerHappyPath() => CircuitBreaker.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> RateLimitHappyPath() => RateLimit.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> ConcurrencyLimitHappyPath() => ConcurrencyLimit.ExecuteAsync(static _ => new ValueTask<int>(42));
}
