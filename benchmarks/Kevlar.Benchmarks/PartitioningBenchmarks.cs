using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Kevlar.Benchmarks;

/// <summary>Warm lookup, cold creation, concurrent key creation, and bounded eviction costs.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PartitioningBenchmarks
{
    private const int ConcurrentLookupsPerInvoke = 1_024;

    private static readonly PartitionedShield<int> ConcurrentWarm = new(
        static _ => Shield.Retry(0, Backoff.None));

    private readonly PartitionedShield<int> _warm = new(
        static _ => Shield.Retry(0, Backoff.None));
    private readonly PartitionedShield<int> _warmWithIdleExpiration = new(
        static _ => Shield.Retry(0, Backoff.None),
        new PartitionedShieldOptions<int>
        {
            IdleExpiration = TimeSpan.FromMinutes(10),
        });
    private readonly PartitionedShield<int> _evicting = new(
        static _ => Shield.Retry(0, Backoff.None),
        new PartitionedShieldOptions<int> { MaxPartitions = 1 });

    private int _evictionKey;

    public PartitioningBenchmarks()
    {
        _ = ConcurrentWarm.GetShield(42);
        _ = _warm.GetShield(42);
        _ = _warmWithIdleExpiration.GetShield(42);
        _ = _evicting.GetShield(0);
    }

    [BenchmarkCategory("WarmLookup"), Benchmark(Baseline = true)]
    public Shield Warm_Lookup() => _warm.GetShield(42);

    [BenchmarkCategory("WarmLookup"), Benchmark]
    public Shield Warm_Lookup_With_Idle_Expiration() =>
        _warmWithIdleExpiration.GetShield(42);

    [BenchmarkCategory("WarmLookupContended"), Benchmark(OperationsPerInvoke = ConcurrentLookupsPerInvoke)]
    public int Warm_Concurrent_Lookups()
    {
        Parallel.For(
            fromInclusive: 0,
            toExclusive: ConcurrentLookupsPerInvoke,
            static _ => ConcurrentWarm.GetShield(42));
        return ConcurrentWarm.Count;
    }

    [BenchmarkCategory("FirstCreation"), Benchmark]
    public Shield Cold_FirstCreation()
    {
        var provider = new PartitionedShield<int>(static _ => Shield.Retry(0, Backoff.None));
        return provider.GetShield(42);
    }

    [BenchmarkCategory("HighKeyConcurrency"), Benchmark]
    public int High_Key_Concurrency()
    {
        var provider = new PartitionedShield<int>(static _ => Shield.Retry(0, Backoff.None));
        Parallel.For(0, Environment.ProcessorCount, key => provider.GetShield(key));
        return provider.Count;
    }

    [BenchmarkCategory("CapacityEviction"), Benchmark]
    public Shield Capacity_Eviction() => _evicting.GetShield(++_evictionKey);
}
