using System.Diagnostics.Metrics;
using BenchmarkDotNet.Attributes;

namespace Kevlar.Benchmarks;

/// <summary>
/// Measures stateful strategy execution with state instruments subscribed while one or many
/// workers contend for the same strategy instance.
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class StateMetricsContentionBenchmarks
{
    private static readonly Shield CircuitBreaker =
        Shield.CircuitBreaker(1_000_000, TimeSpan.FromMinutes(1)).WithName("contention");
    private static readonly Shield ConcurrencyLimit =
        Shield.ConcurrencyLimit(1024).WithName("contention");
    private static readonly Shield RateLimit =
        Shield.RateLimit(1_000_000_000, TimeSpan.FromSeconds(1)).WithName("contention");

    private MeterListener? _listener;

    [Params(1, 16)]
    public int WorkerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
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

        CircuitBreaker.Execute(static _ => { });
        ConcurrencyLimit.Execute(static _ => { });
        RateLimit.Execute(static _ => { });
    }

    [GlobalCleanup]
    public void Cleanup() => _listener?.Dispose();

    [Benchmark(Baseline = true)]
    public void CircuitBreakerExecution() =>
        Parallel.For(0, WorkerCount, static _ => CircuitBreaker.Execute(static _ => { }));

    [Benchmark]
    public void ConcurrencyLimitExecution() =>
        Parallel.For(0, WorkerCount, static _ => ConcurrencyLimit.Execute(static _ => { }));

    [Benchmark]
    public void RateLimitExecution() =>
        Parallel.For(0, WorkerCount, static _ => RateLimit.Execute(static _ => { }));
}
