namespace Kevlar;

/// <summary>Describes a newly created result-aware partition.</summary>
public readonly struct PartitionCreatedEvent<TKey, TResult>
    where TKey : notnull
{
    internal PartitionCreatedEvent(TKey key, Shield<TResult> shield)
    {
        Key = key;
        Shield = shield;
    }

    /// <summary>Gets the partition key.</summary>
    public TKey Key { get; }

    /// <summary>Gets the created result-aware shield.</summary>
    public Shield<TResult> Shield { get; }
}
