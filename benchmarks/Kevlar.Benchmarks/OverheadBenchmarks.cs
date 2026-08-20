using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;

namespace Kevlar.Benchmarks;

/// <summary>
/// Baseline overhead: what an empty pipeline costs per execution, for the async path,
/// the zero-closure state-passing overloads, and the synchronous path.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class OverheadBenchmarks
{
    private static readonly Shield KevlarEmpty = Shield.Empty;
    private static readonly ResiliencePipeline PollyEmpty = ResiliencePipeline.Empty;

    private readonly int _state = 42;

    [BenchmarkCategory("Empty"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_Empty() => KevlarEmpty.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Empty"), Benchmark]
    public ValueTask<int> Polly_Empty() => PollyEmpty.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("EmptyState"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_EmptyState() =>
        KevlarEmpty.ExecuteAsync(_state, static (s, _) => new ValueTask<int>(s));

    [BenchmarkCategory("EmptyState"), Benchmark]
    public ValueTask<int> Polly_EmptyState() =>
        PollyEmpty.ExecuteAsync(static (s, _) => new ValueTask<int>(s), _state);

    [BenchmarkCategory("EmptySync"), Benchmark(Baseline = true)]
    public int Kevlar_EmptySync() => KevlarEmpty.Execute(static _ => 42);

    [BenchmarkCategory("EmptySync"), Benchmark]
    public int Polly_EmptySync() => PollyEmpty.Execute(static _ => 42);
}
