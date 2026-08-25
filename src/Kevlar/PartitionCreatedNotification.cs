namespace Kevlar;

/// <summary>Describes a newly created partition.</summary>
public readonly struct PartitionCreatedNotification
{
    internal PartitionCreatedNotification(object key, object shield)
    {
        Key = key;
        Shield = shield;
    }

    /// <summary>Gets the partition key.</summary>
    public object Key { get; }

    /// <summary>Gets the created <see cref="Kevlar.Shield"/> or result-aware shield.</summary>
    public object Shield { get; }
}
