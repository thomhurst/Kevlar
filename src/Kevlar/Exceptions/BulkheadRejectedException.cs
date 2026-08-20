namespace Kevlar;

/// <summary>Thrown when a bulkhead strategy rejects an execution because both the concurrency and queue limits are full.</summary>
public sealed class BulkheadRejectedException : KevlarException
{
    /// <summary>Initializes the exception.</summary>
    public BulkheadRejectedException()
        : base("The bulkhead's concurrency and queue limits are both full.")
    {
    }
}
