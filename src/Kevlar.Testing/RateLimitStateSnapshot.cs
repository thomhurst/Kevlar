namespace Kevlar.Testing;

/// <summary>An immutable rate-limiter state snapshot.</summary>
public sealed class RateLimitStateSnapshot : StrategyStateSnapshot
{
    internal RateLimitStateSnapshot(int strategyIndex, long availablePermits, int queuedExecutions)
        : base(StrategyKind.RateLimit, strategyIndex)
    {
        AvailablePermits = availablePermits;
        QueuedExecutions = queuedExecutions;
    }

    /// <summary>Gets the estimated number of immediately available permits.</summary>
    public long AvailablePermits { get; }

    /// <summary>Gets the number of executions currently waiting for a permit.</summary>
    public int QueuedExecutions { get; }
}
