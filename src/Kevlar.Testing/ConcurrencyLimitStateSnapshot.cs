namespace Kevlar.Testing;

/// <summary>An immutable concurrency-limiter state snapshot.</summary>
public sealed class ConcurrencyLimitStateSnapshot : StrategyStateSnapshot
{
    internal ConcurrencyLimitStateSnapshot(
        int strategyIndex,
        int availablePermits,
        int runningExecutions,
        int queuedExecutions)
        : base(StrategyKind.ConcurrencyLimit, strategyIndex)
    {
        AvailablePermits = availablePermits;
        RunningExecutions = runningExecutions;
        QueuedExecutions = queuedExecutions;
    }

    /// <summary>Gets the number of permits currently available.</summary>
    public int AvailablePermits { get; }

    /// <summary>Gets the number of admitted executions currently running.</summary>
    public int RunningExecutions { get; }

    /// <summary>Gets the number of executions currently waiting for a permit.</summary>
    public int QueuedExecutions { get; }

}
