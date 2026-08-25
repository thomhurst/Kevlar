namespace Kevlar.Internal;

internal readonly record struct PartitionCacheState(
    int Count,
    long CreatedCount,
    long CapacityEvictionCount,
    long ExpirationEvictionCount,
    long ClearedEvictionCount);
