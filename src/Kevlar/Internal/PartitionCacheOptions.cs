namespace Kevlar.Internal;

internal sealed class PartitionCacheOptions<TKey, TShield>
    where TKey : notnull
    where TShield : class
{
    public PartitionCacheOptions(
        int maxPartitions,
        TimeSpan? idleExpiration,
        TimeProvider timeProvider,
        Func<TKey, TShield, ValueTask>? onCreated,
        Func<TKey, TShield, PartitionEvictionReason, ValueTask>? onEvicted,
        bool ownsStrategies = true)
    {
        MaxPartitions = maxPartitions;
        IdleExpiration = idleExpiration;
        TimeProvider = timeProvider;
        OnCreated = onCreated;
        OnEvicted = onEvicted;
        OwnsStrategies = ownsStrategies;
    }

    public int MaxPartitions { get; }

    public TimeSpan? IdleExpiration { get; }

    public TimeProvider TimeProvider { get; }

    public Func<TKey, TShield, ValueTask>? OnCreated { get; }

    public Func<TKey, TShield, PartitionEvictionReason, ValueTask>? OnEvicted { get; }

    public bool OwnsStrategies { get; }
}
