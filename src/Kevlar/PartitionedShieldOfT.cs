using System.Diagnostics.CodeAnalysis;
using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// Selects and retains an independent result-aware <see cref="Shield{TResult}"/> for each partition key.
/// </summary>
/// <remarks>
/// Retention is bounded by <see cref="PartitionedShieldOptions{TKey, TResult}.MaxPartitions"/>.
/// After the eviction callback, disposable strategies owned by an evicted shield are disposed. Do
/// not reuse a shield after its partition is evicted. A later lookup creates a fresh partition with
/// fresh strategy state. Partition keys are never added to metric tags or shield names automatically.
/// </remarks>
public sealed class PartitionedShield<TKey, TResult> : IDisposable, IAsyncDisposable
    where TKey : notnull
{
    private readonly PartitionCache<TKey, Shield<TResult>> _cache;

    /// <summary>Creates a bounded result-aware partition provider.</summary>
    public PartitionedShield(
        Func<TKey, Shield<TResult>> factory,
        PartitionedShieldOptions<TKey, TResult>? options = null,
        IEqualityComparer<TKey>? comparer = null) =>
        _cache = new PartitionCache<TKey, Shield<TResult>>(
            Wrap(factory),
            (options ?? new PartitionedShieldOptions<TKey, TResult>()).Snapshot(),
            comparer);

    private PartitionedShield(
        Func<TKey, ValueTask<Shield<TResult>>> factory,
        PartitionedShieldOptions<TKey, TResult>? options,
        IEqualityComparer<TKey>? comparer) =>
        _cache = new PartitionCache<TKey, Shield<TResult>>(
            factory,
            (options ?? new PartitionedShieldOptions<TKey, TResult>()).Snapshot(),
            comparer);

    /// <summary>Creates a bounded result-aware partition provider with an asynchronous factory.</summary>
    public static PartitionedShield<TKey, TResult> CreateAsync(
        Func<TKey, ValueTask<Shield<TResult>>> factory,
        PartitionedShieldOptions<TKey, TResult>? options = null,
        IEqualityComparer<TKey>? comparer = null) =>
        new(factory, options, comparer);

    /// <summary>Gets or creates the result-aware shield for <paramref name="key"/>.</summary>
    public Shield<TResult> GetShield(TKey key) => _cache.Get(key);

    /// <summary>Asynchronously gets or creates the result-aware shield for <paramref name="key"/>.</summary>
    public ValueTask<Shield<TResult>> GetShieldAsync(TKey key) => _cache.GetAsync(key);

    /// <summary>Gets a retained result-aware shield without creating one.</summary>
    public bool TryGetShield(TKey key, [NotNullWhen(true)] out Shield<TResult>? shield) =>
        _cache.TryGet(key, out shield);

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

    /// <summary>Disposes strategies owned by every retained partition.</summary>
    public void Dispose() => _cache.Dispose();

    /// <summary>Asynchronously disposes strategies owned by every retained partition.</summary>
    public ValueTask DisposeAsync() => _cache.DisposeAsync();

    private static Func<TKey, ValueTask<Shield<TResult>>> Wrap(Func<TKey, Shield<TResult>> factory)
    {
        Throw.IfNull(factory, nameof(factory));
        return key => new ValueTask<Shield<TResult>>(factory(key));
    }
}
