namespace Kevlar;

/// <summary>Describes a newly created partition.</summary>
public readonly struct PartitionCreatedEvent<TKey>
    where TKey : notnull
{
    internal PartitionCreatedEvent(TKey key, Shield shield)
    {
        Key = key;
        Shield = shield;
    }

    /// <summary>Gets the partition key.</summary>
    public TKey Key { get; }

    /// <summary>Gets the created shield.</summary>
    public Shield Shield { get; }
}
