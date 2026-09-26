namespace Kevlar.Testing;

/// <summary>An immutable rate-limiter state snapshot.</summary>
public sealed class RateLimitStateSnapshot : StrategyStateSnapshot
{
    internal RateLimitStateSnapshot(int strategyIndex, long availablePermits, int queuedExecutions,
        IReadOnlyDictionary<int, int>? queuedByPriority = null)
        : base(StrategyKind.RateLimit, strategyIndex)
    {
        AvailablePermits = availablePermits;
        QueuedExecutions = queuedExecutions;
        QueuedByPriority = queuedByPriority ?? EmptyPriorities;
    }

    /// <summary>Gets the estimated number of immediately available permits.</summary>
    public long AvailablePermits { get; }

    /// <summary>Gets the number of executions currently waiting for a permit.</summary>
    public int QueuedExecutions { get; }

    /// <summary>Gets immutable queued counts by priority; empty when priority queues are disabled.</summary>
    public IReadOnlyDictionary<int, int> QueuedByPriority { get; }

    private static readonly IReadOnlyDictionary<int, int> EmptyPriorities =
        new System.Collections.ObjectModel.ReadOnlyDictionary<int, int>(new Dictionary<int, int>());
}
