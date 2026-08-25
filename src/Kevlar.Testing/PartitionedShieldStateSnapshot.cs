namespace Kevlar.Testing;

/// <summary>Captures the retention and eviction state of a partitioned shield provider.</summary>
public sealed class PartitionedShieldStateSnapshot
{
    internal PartitionedShieldStateSnapshot(
        int count,
        long createdCount,
        long capacityEvictionCount,
        long expirationEvictionCount,
        long clearedEvictionCount)
    {
        Count = count;
        CreatedCount = createdCount;
        CapacityEvictionCount = capacityEvictionCount;
        ExpirationEvictionCount = expirationEvictionCount;
        ClearedEvictionCount = clearedEvictionCount;
    }

    /// <summary>Gets the snapshot contract version.</summary>
    public int ContractVersion => 1;

    /// <summary>Gets the number of retained partitions.</summary>
    public int Count { get; }

    /// <summary>Gets the number of created partitions.</summary>
    public long CreatedCount { get; }

    /// <summary>Gets the number evicted to enforce capacity.</summary>
    public long CapacityEvictionCount { get; }

    /// <summary>Gets the number evicted after idle expiration.</summary>
    public long ExpirationEvictionCount { get; }

    /// <summary>Gets the number removed explicitly.</summary>
    public long ClearedEvictionCount { get; }

    /// <summary>Gets the number removed for any reason.</summary>
    public long EvictionCount =>
        CapacityEvictionCount + ExpirationEvictionCount + ClearedEvictionCount;
}
