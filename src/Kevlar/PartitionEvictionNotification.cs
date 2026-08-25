namespace Kevlar;

/// <summary>Describes a partition removed from a provider.</summary>
public readonly struct PartitionEvictionNotification
{
    internal PartitionEvictionNotification(object key, object shield, PartitionEvictionReason reason)
    {
        Key = key;
        Shield = shield;
        Reason = reason;
    }

    /// <summary>Gets the partition key.</summary>
    public object Key { get; }

    /// <summary>Gets the removed <see cref="Kevlar.Shield"/> or result-aware shield.</summary>
    public object Shield { get; }

    /// <summary>Gets why the partition was removed.</summary>
    public PartitionEvictionReason Reason { get; }
}
