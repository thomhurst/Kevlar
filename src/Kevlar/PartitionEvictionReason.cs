namespace Kevlar;

/// <summary>Identifies why a partition was removed from a provider.</summary>
public enum PartitionEvictionReason
{
    /// <summary>The least-recently-used partition was removed to enforce the capacity bound.</summary>
    Capacity,

    /// <summary>The partition exceeded its configured idle duration.</summary>
    Idle,

    /// <summary>The partition was explicitly removed or cleared.</summary>
    Cleared,
}
