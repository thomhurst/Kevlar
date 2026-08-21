using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Kevlar.Chaos;

namespace Kevlar.Benchmarks;

/// <summary>Measures disabled, excluded, and injected chaos paths against an empty shield.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ChaosBenchmarks
{
    private static readonly Shield Empty = Shield.Empty;
    private static readonly Shield Disabled = ChaosShield.Fault(static _ => { });
    private static readonly Shield Excluded = ChaosShield.Fault(static options =>
    {
        options.Enabled = true;
        options.InjectionRate = 0;
    });
    private static readonly Shield Latency = ChaosShield.Latency(static options => options.Enabled = true);
    private static readonly Shield<int> Outcome = ChaosShield.Outcome<int>(static options =>
    {
        options.Enabled = true;
        options.Result = 42;
    });
    private static readonly Shield Behavior = ChaosShield.Behavior(static options =>
    {
        options.Enabled = true;
        options.Behavior = static _ => ValueTask.CompletedTask;
    });

    /// <summary>Executes an empty shield baseline.</summary>
    [BenchmarkCategory("PassThrough"), Benchmark(Baseline = true)]
    public ValueTask<int> Empty_Shield() => Empty.ExecuteAsync(static _ => new ValueTask<int>(42));

    /// <summary>Executes a default-disabled chaos strategy.</summary>
    [BenchmarkCategory("PassThrough"), Benchmark]
    public ValueTask<int> Disabled_Chaos() => Disabled.ExecuteAsync(static _ => new ValueTask<int>(42));

    /// <summary>Executes enabled chaos excluded by a zero injection rate.</summary>
    [BenchmarkCategory("PassThrough"), Benchmark]
    public ValueTask<int> Excluded_Chaos() => Excluded.ExecuteAsync(static _ => new ValueTask<int>(42));

    /// <summary>Injects zero-duration latency.</summary>
    [BenchmarkCategory("Injected"), Benchmark(Baseline = true)]
    public ValueTask<int> Zero_Latency() => Latency.ExecuteAsync(static _ => new ValueTask<int>(42));

    /// <summary>Injects a typed result.</summary>
    [BenchmarkCategory("Injected"), Benchmark]
    public ValueTask<int> Typed_Outcome() => Outcome.ExecuteAsync(static _ => new ValueTask<int>(0));

    /// <summary>Injects synchronously completing custom behavior.</summary>
    [BenchmarkCategory("Injected"), Benchmark]
    public ValueTask<int> Completed_Behavior() => Behavior.ExecuteAsync(static _ => new ValueTask<int>(42));
}
