using System.Diagnostics.CodeAnalysis;
using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// Selects and retains an independent untyped <see cref="Shield"/> for each partition key.
/// </summary>
/// <remarks>
/// Retention is bounded by <see cref="PartitionedShieldOptions.MaximumPartitions"/>. Eviction
/// removes only the provider's reference: callers that already hold the old shield, including
/// active executions, continue normally. A later lookup creates a fresh partition with fresh
/// strategy state. Partition keys are never added to metric tags or shield names automatically.
/// </remarks>
public sealed class PartitionedShield<TKey>
    where TKey : notnull
{
    private readonly PartitionCache<TKey, Shield> _cache;

    /// <summary>Creates a bounded partition provider.</summary>
    public PartitionedShield(
        Func<TKey, Shield> factory,
        PartitionedShieldOptions? options = null,
        IEqualityComparer<TKey>? comparer = null) =>
        _cache = new PartitionCache<TKey, Shield>(Wrap(factory), options, comparer);

    private PartitionedShield(
        Func<TKey, ValueTask<Shield>> factory,
        PartitionedShieldOptions? options,
        IEqualityComparer<TKey>? comparer) =>
        _cache = new PartitionCache<TKey, Shield>(factory, options, comparer);

    /// <summary>Creates a bounded partition provider with an asynchronous factory.</summary>
    public static PartitionedShield<TKey> CreateAsync(
        Func<TKey, ValueTask<Shield>> factory,
        PartitionedShieldOptions? options = null,
        IEqualityComparer<TKey>? comparer = null) =>
        new(factory, options, comparer);

    /// <summary>Gets or creates the shield for <paramref name="key"/>.</summary>
    public Shield GetShield(TKey key) => _cache.Get(key);

    /// <summary>Asynchronously gets or creates the shield for <paramref name="key"/>.</summary>
    public ValueTask<Shield> GetShieldAsync(TKey key) => _cache.GetAsync(key);

    /// <summary>Gets a retained shield without creating one.</summary>
    public bool TryGetShield(TKey key, [NotNullWhen(true)] out Shield? shield) =>
        _cache.TryGet(key, out shield);

    /// <summary>Removes a retained partition. Existing users of its shield are unaffected.</summary>
    public bool Remove(TKey key) => TryRemove(key);

    /// <summary>Removes a retained partition. Existing users of its shield are unaffected.</summary>
    public bool TryRemove(TKey key) => _cache.TryRemove(key);

    /// <summary>Asynchronously removes a retained partition.</summary>
    public ValueTask<bool> TryRemoveAsync(TKey key) => _cache.TryRemoveAsync(key);

    /// <summary>Removes every retained partition. Existing users of those shields are unaffected.</summary>
    public void Clear() => _cache.Clear();

    /// <summary>Asynchronously removes every retained partition.</summary>
    public ValueTask ClearAsync() => _cache.ClearAsync();

    /// <summary>Removes partitions whose configured idle expiration has elapsed.</summary>
    /// <returns>The number removed.</returns>
    public int PruneExpired() => _cache.PruneExpired();

    /// <summary>Asynchronously removes partitions whose idle expiration has elapsed.</summary>
    public ValueTask<int> PruneExpiredAsync() => _cache.PruneExpiredAsync();

    /// <summary>Gets the current number of retained partitions.</summary>
    public int Count => _cache.Count;

    /// <summary>Gets the total number of partitions created by this provider.</summary>
    public long CreatedCount => _cache.CreatedCount;

    /// <summary>Gets the total number evicted to enforce the capacity bound.</summary>
    public long CapacityEvictionCount => _cache.CapacityEvictionCount;

    /// <summary>Gets the total number evicted after their idle expiration elapsed.</summary>
    public long ExpirationEvictionCount => _cache.ExpirationEvictionCount;

    /// <summary>Gets the total number removed explicitly through remove or clear operations.</summary>
    public long ClearedEvictionCount => _cache.ClearedEvictionCount;

    /// <summary>Gets the total number of partitions removed for any reason.</summary>
    public long EvictionCount => _cache.EvictionCount;

    internal PartitionCacheState CaptureState() => _cache.CaptureState();

    private static Func<TKey, ValueTask<Shield>> Wrap(Func<TKey, Shield> factory)
    {
        Throw.IfNull(factory, nameof(factory));
        return key => new ValueTask<Shield>(factory(key));
    }
}
