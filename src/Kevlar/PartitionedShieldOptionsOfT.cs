using Kevlar.Internal;

namespace Kevlar;

/// <summary>Controls retention for a result-aware partitioned shield provider.</summary>
public sealed class PartitionedShieldOptions<TKey, TResult>
    where TKey : notnull
{
    /// <summary>
    /// Gets or sets the maximum number of retained partitions. The least recently used partition
    /// is evicted before this bound is exceeded. Defaults to 1,000.
    /// </summary>
    public int MaxPartitions { get; set; } = 1_000;

    /// <summary>
    /// Gets or sets the optional idle duration after which a partition may be evicted on the next
    /// provider operation. A null value disables idle expiry.
    /// </summary>
    public TimeSpan? IdleExpiration { get; set; }

    /// <summary>Gets or sets the time source used for idle expiry. Defaults to system time.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// Gets or sets whether the provider owns and disposes strategies returned by its factory.
    /// Defaults to <see langword="true"/>. Set to <see langword="false"/> when those strategy
    /// instances are also used by shields outside this provider.
    /// </summary>
    public bool OwnsStrategies { get; set; } = true;

    /// <summary>
    /// Invoked and awaited after a partition is created and retained. Return
    /// <see langword="default"/> from a synchronous callback.
    /// </summary>
    public Func<PartitionCreatedEvent<TKey, TResult>, ValueTask>? OnCreated { get; set; }

    /// <summary>
    /// Invoked and awaited after a partition is removed from the provider and before an
    /// automatically evicted partition's slot is reused. A cold lookup reentered from this
    /// callback may return an unretained shield when the invoking eviction owns all available
    /// capacity. Disposing this provider from its own callback is rejected to prevent a lifecycle
    /// deadlock. Return <see langword="default"/> from a synchronous callback.
    /// </summary>
    public Func<PartitionEvictedEvent<TKey, TResult>, ValueTask>? OnEvicted { get; set; }

    internal PartitionCacheOptions<TKey, Shield<TResult>> Snapshot()
    {
        var onCreated = OnCreated;
        var onEvicted = OnEvicted;
        return new PartitionCacheOptions<TKey, Shield<TResult>>(
            MaxPartitions,
            IdleExpiration,
            TimeProvider,
            onCreated is null
                ? null
                : (key, shield) => onCreated(new PartitionCreatedEvent<TKey, TResult>(key, shield)),
            onEvicted is null
                ? null
                : (key, shield, reason) => onEvicted(
                    new PartitionEvictedEvent<TKey, TResult>(key, shield, reason)),
            OwnsStrategies);
    }
}
