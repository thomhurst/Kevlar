using BenchmarkDotNet.Attributes;

namespace Kevlar.Benchmarks;

/// <summary>
/// Stable pre-deadline API fixtures for baseline/candidate comparisons. These exercise default
/// behavior and pooled-context propagation without requiring the new option on the baseline.
/// </summary>
[MemoryDiagnoser]
public class DeadlineOverheadBenchmarks
{
    private static readonly Shield Timeout = Shield.Timeout(TimeSpan.FromMinutes(1));
    private static readonly Shield Retry = Shield.Retry(3, Backoff.None);
    private static readonly Shield<int> ResultRetry = Shield.For<int>().Retry(options =>
    {
        options.MaxRetries = 1;
        options.Backoff = Backoff.None;
        options.HandlesResult = static result => result == 0;
    });
    private static readonly Shield NestedTimeout = Timeout.Timeout(TimeSpan.FromSeconds(30));
    private static readonly Shield<int> Hedge = Shield.For<int>().Hedge(1, delay: TimeSpan.FromSeconds(10));
    private readonly Counter _counter = new();

    [Benchmark]
    public ValueTask<int> Empty() => Shield.Empty.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> TimeoutSuccess() => Timeout.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> NestedTimeoutSuccess() => NestedTimeout.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> RetrySuccess() => Retry.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> RetryHandledResult() => ResultRetry.ExecuteAsync(_counter,
        static (counter, _) => new ValueTask<int>(++counter.Value % 2));

    [Benchmark]
    public ValueTask<int> HedgePrimaryWins() => Hedge.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> ExplicitChild() => Timeout.ExecuteWithContextAsync(static parent =>
        Shield.Empty.ExecuteWithContextAsync(parent, static _ => new ValueTask<int>(42)));

    private sealed class Counter
    {
        public int Value;
    }
}
