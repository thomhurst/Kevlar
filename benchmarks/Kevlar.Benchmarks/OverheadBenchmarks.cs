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
    private static readonly KevlarKey<OverheadBenchmarks> MetadataState = new("metadata-state");
    private static readonly Shield KevlarEmpty = Shield.Empty;
    private static readonly ResiliencePipeline PollyEmpty = ResiliencePipeline.Empty;
    private static readonly Task<int> CompletedStateTask = Task.FromResult(42);

    private readonly int _state = 42;

    [BenchmarkCategory("Empty"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_Empty() => KevlarEmpty.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("Empty"), Benchmark]
    public ValueTask<int> Polly_Empty() => PollyEmpty.ExecuteAsync(static _ => new ValueTask<int>(42));

    [BenchmarkCategory("EmptyState"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_EmptyState() =>
        KevlarEmpty.ExecuteAsync(_state, static (s, _) => new ValueTask<int>(s));

    [BenchmarkCategory("EmptyContextState"), Benchmark(Baseline = true)]
    public ValueTask<int> Kevlar_EmptyReferenceState() =>
        KevlarEmpty.ExecuteAsync(this, static (state, _) => new ValueTask<int>(state._state));

    [BenchmarkCategory("EmptyContextState"), Benchmark]
    public ValueTask<int> Kevlar_EmptyContextState() =>
        KevlarEmpty.ExecuteWithContextAsync(
            this,
            static (state, properties) => properties.Set(MetadataState, state),
            static (_, context) => new ValueTask<int>(
                context.Properties.GetOrDefault<OverheadBenchmarks>(MetadataState)!._state));

    [BenchmarkCategory("EmptyState"), Benchmark]
    public ValueTask<int> Polly_EmptyState() =>
        PollyEmpty.ExecuteAsync(static (s, _) => new ValueTask<int>(s), _state);

    /// <summary>Executes a state-passing no-throw call through an empty Kevlar pipeline.</summary>
    [BenchmarkCategory("EmptyOutcomeState"), Benchmark(
        Baseline = true,
        Description = "Kevlar empty shield outcome with caller state")]
    public ValueTask<Outcome<int>> Kevlar_EmptyOutcomeState() =>
        KevlarEmpty.ExecuteOutcomeAsync(_state, static (s, _) => new ValueTask<int>(s));

    /// <summary>Executes a state-passing <see cref="Task{TResult}"/> no-throw call through an empty Kevlar pipeline.</summary>
    [BenchmarkCategory("EmptyOutcomeState"), Benchmark]
    public ValueTask<Outcome<int>> Kevlar_EmptyTaskOutcomeState() =>
        KevlarEmpty.ExecuteOutcomeAsync(_state, static (_, _) => CompletedStateTask);

    [BenchmarkCategory("EmptySync"), Benchmark(Baseline = true)]
    public int Kevlar_EmptySync() => KevlarEmpty.Execute(static _ => 42);

    [BenchmarkCategory("EmptySync"), Benchmark]
    public int Polly_EmptySync() => PollyEmpty.Execute(static _ => 42);

    [BenchmarkCategory("NestedEmptyAsync"), Benchmark]
    public ValueTask<int> Kevlar_NestedEmptyAsync() =>
        KevlarEmpty.ExecuteWithContextAsync(
            static parentContext => KevlarEmpty.ExecuteWithContextAsync(
                parentContext,
                static _ => new ValueTask<int>(42)));

    [BenchmarkCategory("NestedEmptySync"), Benchmark]
    public int Kevlar_NestedEmptySync() =>
        KevlarEmpty.ExecuteWithContext(
            static parentContext => KevlarEmpty.ExecuteWithContext(parentContext, static _ => 42));
}
