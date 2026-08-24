using System.Diagnostics.CodeAnalysis;
using Kevlar.Internal;

namespace Kevlar;

/// <summary>Selects and retains an independent <see cref="VoidShield"/> for each partition key.</summary>
/// <remarks>
/// Retention is bounded by <see cref="PartitionedShieldOptions.MaximumPartitions"/>. Eviction
/// removes only the provider's reference; active executions continue normally.
/// </remarks>
public sealed class PartitionedVoidShield<TKey>
    where TKey : notnull
{
    private readonly PartitionCache<TKey, VoidShield> _cache;

    /// <summary>Creates a bounded partition provider.</summary>
    public PartitionedVoidShield(
        Func<TKey, VoidShield> factory,
        PartitionedShieldOptions? options = null,
        IEqualityComparer<TKey>? comparer = null) =>
        _cache = new PartitionCache<TKey, VoidShield>(factory, options, comparer);

    /// <summary>Gets or creates the shield for <paramref name="key"/>.</summary>
    public VoidShield GetShield(TKey key) => _cache.Get(key);

    /// <summary>Gets a retained shield without creating one.</summary>
    public bool TryGetShield(TKey key, [NotNullWhen(true)] out VoidShield? shield) =>
        _cache.TryGet(key, out shield);

    /// <summary>Removes a retained partition.</summary>
    public bool Remove(TKey key) => _cache.Remove(key);

    /// <summary>Removes every retained partition.</summary>
    public void Clear() => _cache.Clear();

    /// <summary>Removes partitions whose configured idle expiration has elapsed.</summary>
    public int PruneExpired() => _cache.PruneExpired();

    /// <summary>Gets the current number of retained partitions.</summary>
    public int Count => _cache.Count;

    /// <summary>Gets the total number of partitions created.</summary>
    public long CreatedCount => _cache.CreatedCount;

    /// <summary>Gets the total number evicted to enforce capacity.</summary>
    public long CapacityEvictionCount => _cache.CapacityEvictionCount;

    /// <summary>Gets the total number evicted after idle expiration.</summary>
    public long ExpirationEvictionCount => _cache.ExpirationEvictionCount;
}
