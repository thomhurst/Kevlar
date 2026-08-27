namespace Kevlar;

/// <summary>Describes a result-aware partition removed from a provider.</summary>
public readonly struct PartitionEvictedEvent<TKey, TResult>
    where TKey : notnull
{
    internal PartitionEvictedEvent(TKey key, Shield<TResult> shield, PartitionEvictionReason reason)
    {
        Key = key;
        Shield = shield;
        Reason = reason;
    }

    /// <summary>Gets the partition key.</summary>
    public TKey Key { get; }

    /// <summary>Gets the removed result-aware shield.</summary>
    public Shield<TResult> Shield { get; }

    /// <summary>Gets why the partition was removed.</summary>
    public PartitionEvictionReason Reason { get; }
}
