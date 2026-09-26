namespace Kevlar.Testing;

/// <summary>An immutable concurrency-limiter state snapshot.</summary>
public sealed class ConcurrencyLimitStateSnapshot : StrategyStateSnapshot
{
    internal ConcurrencyLimitStateSnapshot(
        int strategyIndex,
        int availablePermits,
        int runningExecutions,
        int queuedExecutions,
        int currentLimit,
        IReadOnlyDictionary<int, int>? queuedByPriority = null)
        : base(StrategyKind.ConcurrencyLimit, strategyIndex)
    {
        AvailablePermits = availablePermits;
        RunningExecutions = runningExecutions;
        QueuedExecutions = queuedExecutions;
        QueuedByPriority = queuedByPriority ?? EmptyPriorities;
        CurrentLimit = currentLimit;
    }

    /// <summary>Gets the number of permits currently available.</summary>
    public int AvailablePermits { get; }

    /// <summary>Gets the number of admitted executions currently running.</summary>
    public int RunningExecutions { get; }

    /// <summary>Gets the number of executions currently waiting for a permit.</summary>
    public int QueuedExecutions { get; }

    /// <summary>Gets immutable queued counts by priority; empty when priority queues are disabled.</summary>
    public IReadOnlyDictionary<int, int> QueuedByPriority { get; }

    private static readonly IReadOnlyDictionary<int, int> EmptyPriorities =
        new System.Collections.ObjectModel.ReadOnlyDictionary<int, int>(new Dictionary<int, int>());

    /// <summary>Gets the current admission limit, including adaptive adjustments.</summary>
    /// <remarks>Running executions can temporarily exceed a reduced limit while existing calls finish.</remarks>
    public int CurrentLimit { get; }

}
