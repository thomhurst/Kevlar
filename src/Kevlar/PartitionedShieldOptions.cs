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
}
