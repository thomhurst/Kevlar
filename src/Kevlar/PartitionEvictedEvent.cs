namespace Kevlar;

/// <summary>Describes a partition removed from a provider.</summary>
public readonly struct PartitionEvictedEvent<TKey>
    where TKey : notnull
{
    internal PartitionEvictedEvent(TKey key, Shield shield, PartitionEvictionReason reason)
    {
        Key = key;
        Shield = shield;
        Reason = reason;
    }

    /// <summary>Gets the partition key.</summary>
    public TKey Key { get; }

    /// <summary>Gets the removed shield.</summary>
    public Shield Shield { get; }

    /// <summary>Gets why the partition was removed.</summary>
    public PartitionEvictionReason Reason { get; }
}
