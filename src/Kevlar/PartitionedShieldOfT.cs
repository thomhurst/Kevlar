using System.Diagnostics.CodeAnalysis;
using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// Selects and retains an independent result-aware <see cref="Shield{TResult}"/> for each partition key.
/// </summary>
/// <remarks>
/// Retention is bounded by <see cref="PartitionedShieldOptions.MaximumPartitions"/>. Eviction
/// removes only the provider's reference: callers that already hold the old shield, including
/// active executions, continue normally. A later lookup creates a fresh partition with fresh
/// strategy state. Partition keys are never added to metric tags or shield names automatically.
/// </remarks>
public sealed class PartitionedShield<TKey, TResult>
    where TKey : notnull
{
    private readonly PartitionCache<TKey, Shield<TResult>> _cache;

    /// <summary>Creates a bounded result-aware partition provider.</summary>
    public PartitionedShield(
        Func<TKey, Shield<TResult>> factory,
        PartitionedShieldOptions? options = null,
        IEqualityComparer<TKey>? comparer = null) =>
        _cache = new PartitionCache<TKey, Shield<TResult>>(factory, options, comparer);

    /// <summary>Gets or creates the result-aware shield for <paramref name="key"/>.</summary>
    public Shield<TResult> GetShield(TKey key) => _cache.Get(key);

    /// <summary>Gets a retained result-aware shield without creating one.</summary>
    public bool TryGetShield(TKey key, [NotNullWhen(true)] out Shield<TResult>? shield) =>
        _cache.TryGet(key, out shield);

    /// <summary>Removes a retained partition. Existing users of its shield are unaffected.</summary>
    public bool Remove(TKey key) => _cache.Remove(key);

    /// <summary>Removes every retained partition. Existing users of those shields are unaffected.</summary>
    public void Clear() => _cache.Clear();

    /// <summary>Removes partitions whose configured idle expiration has elapsed.</summary>
    /// <returns>The number removed.</returns>
    public int PruneExpired() => _cache.PruneExpired();

    /// <summary>Gets the current number of retained partitions.</summary>
    public int Count => _cache.Count;

    /// <summary>Gets the total number of partitions created by this provider.</summary>
    public long CreatedCount => _cache.CreatedCount;

    /// <summary>Gets the total number evicted to enforce the capacity bound.</summary>
    public long CapacityEvictionCount => _cache.CapacityEvictionCount;

    /// <summary>Gets the total number evicted after their idle expiration elapsed.</summary>
    public long ExpirationEvictionCount => _cache.ExpirationEvictionCount;
}
