namespace Kevlar;

/// <summary>Controls retention for a partitioned shield provider.</summary>
public sealed class PartitionedShieldOptions
{
    /// <summary>
    /// Gets or sets the maximum number of retained partitions. The least recently used partition
    /// is evicted before this bound is exceeded. Defaults to 1,000.
    /// </summary>
    public int MaximumPartitions { get; set; } = 1_000;

    /// <summary>
    /// Gets or sets the optional idle duration after which a partition may be evicted on the next
    /// provider operation. A null value disables idle expiry.
    /// </summary>
    public TimeSpan? IdleExpiration { get; set; }

    /// <summary>Gets or sets the time source used for idle expiry. Defaults to system time.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Invoked after a partition is created and retained.</summary>
    public Action<PartitionCreatedNotification>? OnCreated { get; set; }

    /// <summary>Invoked and awaited after <see cref="OnCreated"/>.</summary>
    public Func<PartitionCreatedNotification, ValueTask>? OnCreatedAsync { get; set; }

    /// <summary>Invoked after a partition is removed from the provider.</summary>
    public Action<PartitionEvictionNotification>? OnEvicted { get; set; }

    /// <summary>
    /// Invoked and awaited after <see cref="OnEvicted"/> and before an automatically evicted
    /// partition's slot is reused. A cold lookup reentered from this callback may return an
    /// unretained shield when the invoking eviction owns all available capacity.
    /// </summary>
    public Func<PartitionEvictionNotification, ValueTask>? OnEvictedAsync { get; set; }
}
