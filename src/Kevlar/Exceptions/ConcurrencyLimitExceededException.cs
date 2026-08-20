namespace Kevlar;

/// <summary>Thrown when a concurrency limit strategy rejects an execution because both the concurrency and queue limits are full.</summary>
public sealed class ConcurrencyLimitExceededException : KevlarException
{
    /// <summary>Initializes the exception.</summary>
    public ConcurrencyLimitExceededException()
        : base("The concurrency limit's concurrency and queue limits are both full.")
    {
    }
}
