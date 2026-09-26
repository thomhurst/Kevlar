using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Kevlar.Extensions.Tracing;

namespace Kevlar.Benchmarks;

/// <summary>Measures tracing registration overhead and bounded event enrichment on an existing activity.</summary>
[MemoryDiagnoser]
public class TracingBenchmarks
{
    private static readonly Shield Pipeline = Shield.Retry(1, Backoff.None).WithName("benchmark");
    private IDisposable? _subscription;

    [Params(false, true)]
    public bool Registered { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (Registered)
        {
            _subscription = KevlarTracing.Listen();
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _subscription?.Dispose();

    [Benchmark]
    public ValueTask<int> NoActivity() => Pipeline.ExecuteAsync(static _ => new ValueTask<int>(42));

    // Each invocation owns its activity so recorded events cannot accumulate across benchmark iterations.
    // Both parameter values include identical activity creation/disposal costs.
    [Benchmark]
    public int UnsampledActivity()
    {
        using var activity = new Activity("application").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.ActivityTraceFlags = ActivityTraceFlags.None;
        return Pipeline.Execute(static _ => 42);
    }

    [Benchmark]
    public int SampledActivity()
    {
        using var activity = new Activity("application").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        return Pipeline.Execute(static _ => 42);
    }
}

