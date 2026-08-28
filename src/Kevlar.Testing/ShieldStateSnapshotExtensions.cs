using Kevlar.Strategies;

namespace Kevlar.Testing;

/// <summary>Captures read-only state snapshots from shields.</summary>
public static class ShieldStateSnapshotExtensions
{
    /// <summary>Captures the current state of an untyped shield's stateful strategies.</summary>
    public static ShieldStateSnapshot GetStateSnapshot(this Shield shield)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        var snapshot = shield.CurrentSnapshot;
        return Capture(snapshot.Strategies, snapshot.TimeOrSystem);
    }

    /// <summary>Captures the current state of a typed shield's stateful strategies.</summary>
    public static ShieldStateSnapshot GetStateSnapshot<TResult>(this Shield<TResult> shield)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        var snapshot = shield.CurrentSnapshot;
        return Capture(snapshot.Strategies, snapshot.TimeOrSystem);
    }

    /// <summary>Captures the current retention and eviction state of a partitioned provider.</summary>
    public static PartitionedShieldStateSnapshot GetStateSnapshot<TKey>(
        this PartitionedShield<TKey> shield)
        where TKey : notnull
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        var state = shield.CaptureState();
        return new PartitionedShieldStateSnapshot(
            state.Count,
            state.CreatedCount,
            state.CapacityEvictionCount,
            state.ExpirationEvictionCount,
            state.ClearedEvictionCount);
    }

    /// <summary>Captures the current retention and eviction state of a typed partitioned provider.</summary>
    public static PartitionedShieldStateSnapshot GetStateSnapshot<TKey, TResult>(
        this PartitionedShield<TKey, TResult> shield)
        where TKey : notnull
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        var state = shield.CaptureState();
        return new PartitionedShieldStateSnapshot(
            state.Count,
            state.CreatedCount,
            state.CapacityEvictionCount,
            state.ExpirationEvictionCount,
            state.ClearedEvictionCount);
    }

    private static ShieldStateSnapshot Capture(Strategy[] strategies, TimeProvider timeProvider)
    {
        var snapshots = new List<StrategyStateSnapshot>();
        for (var index = 0; index < strategies.Length; index++)
        {
            var snapshot = Capture(strategies[index], index, timeProvider);
            if (snapshot is not null)
            {
                snapshots.Add(snapshot);
            }
        }

        return new ShieldStateSnapshot(Array.AsReadOnly(snapshots.ToArray()));
    }

    private static StrategyStateSnapshot? Capture(
        Strategy strategy,
        int strategyIndex,
        TimeProvider timeProvider)
    {
        switch (strategy)
        {
            case CircuitBreakerStrategy circuit:
                return new CircuitBreakerStateSnapshot(strategyIndex, circuit.Core.GetState(timeProvider));
            case RateLimitStrategy rateLimit:
                var rate = rateLimit.CaptureState(timeProvider);
                return new RateLimitStateSnapshot(strategyIndex, rate.Available, rate.Queued);
            case ConcurrencyLimitStrategy concurrency:
                var current = concurrency.CaptureState();
                return new ConcurrencyLimitStateSnapshot(
                    strategyIndex,
                    current.Available,
                    current.Running,
                    current.Queued);
            default:
                return null;
        }
    }
}
