using BenchmarkDotNet.Attributes;

namespace Kevlar.Benchmarks;

/// <summary>Structured event overhead for synchronously completing execution paths.</summary>
[MemoryDiagnoser]
public class StructuredEventBenchmarks
{
    private static readonly Shield Empty = Shield.Empty.WithName("benchmark");
    private static readonly Shield Retry = Shield.Retry(0, Backoff.None).WithName("benchmark");

    private IDisposable? _subscription;

    [Params(false, true)]
    public bool ListenerEnabled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (ListenerEnabled)
        {
            _subscription = KevlarDiagnostics.Subscribe(new NoOpListener());
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _subscription?.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask<int> EmptyShield() => Empty.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> RetryHappyPath() => Retry.ExecuteAsync(static _ => new ValueTask<int>(42));

    private sealed class NoOpListener : KevlarEventListener
    {
        public override void OnEvent<T>(in KevlarEvent<T> telemetryEvent)
        {
        }
    }
}
