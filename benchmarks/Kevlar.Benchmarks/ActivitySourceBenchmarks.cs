using System.Diagnostics;
using BenchmarkDotNet.Attributes;

namespace Kevlar.Benchmarks;

/// <summary>Compares disabled tracing with recorded execution/attempt spans, without retaining exported spans.</summary>
[MemoryDiagnoser]
public class ActivitySourceBenchmarks
{
    private static readonly Shield Empty = Shield.Empty.WithName("benchmark");
    private static readonly Shield Retry = Shield.Retry(1, Backoff.None).WithName("benchmark");
    private static readonly Shield Hedge = Shield.Hedge(1, delay: TimeSpan.Zero).WithName("benchmark");
    private ActivityListener? _listener;

    [Params(false, true)]
    public bool ListenerEnabled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (!ListenerEnabled)
        {
            return;
        }
        _listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == KevlarDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    [GlobalCleanup]
    public void Cleanup() => _listener?.Dispose();

    [Benchmark]
    public ValueTask<int> EmptyAsync() => Empty.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public int EmptySync() => Empty.Execute(static _ => 42);

    [Benchmark]
    public ValueTask<int> RetryHappyPath() => Retry.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> HedgePrimaryWins() => Hedge.ExecuteAsync(static _ => new ValueTask<int>(42));
}
