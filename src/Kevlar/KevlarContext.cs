using Reservoir;

namespace Kevlar;

/// <summary>
/// Ambient state for a single execution flowing through a shield pipeline.
/// Contexts are created and pooled by Kevlar automatically; user code observes them
/// in strategy callbacks and never needs to construct or return them.
/// </summary>
public sealed class KevlarContext
{
    internal const int PoolCapacity = 128;

    private static readonly ObjectPool<KevlarContext, PoolPolicy> Pool = new(maxCapacity: PoolCapacity);

    private KevlarContext()
    {
    }

    /// <summary>
    /// The cancellation token for the current scope. Strategies such as timeouts replace this
    /// token for the layers beneath them, which is why executed delegates must use the token
    /// they are handed rather than a captured one.
    /// </summary>
    public CancellationToken CancellationToken { get; internal set; }

    /// <summary><see langword="true"/> when the execution was started through a synchronous <c>Execute</c> call.</summary>
    public bool IsSynchronous { get; internal set; }

    /// <summary>The name of the executing shield, if one was assigned via <c>WithName</c>.</summary>
    public string? ShieldName { get; internal set; }

    internal int StrategyIndex { get; set; }

    /// <summary>The time provider used for delays, timeouts and time-window calculations.</summary>
    public TimeProvider TimeProvider { get; internal set; } = TimeProvider.System;

    /// <summary>Custom properties carried through the execution.</summary>
    public KevlarProperties Properties { get; } = new();

    internal static KevlarContext Rent(CancellationToken cancellationToken, bool isSynchronous, TimeProvider timeProvider, string? shieldName)
    {
        var context = Pool.Rent();

        context.CancellationToken = cancellationToken;
        context.IsSynchronous = isSynchronous;
        context.TimeProvider = timeProvider;
        context.ShieldName = shieldName;
        context.StrategyIndex = -1;
        return context;
    }

    internal static void Return(KevlarContext context) => Pool.Return(context);

    /// <summary>
    /// Creates a detached copy of this context for a concurrent attempt (used by hedging).
    /// The copy shares no mutable state with the original.
    /// </summary>
    internal KevlarContext Fork(CancellationToken cancellationToken)
    {
        var fork = Rent(cancellationToken, IsSynchronous, TimeProvider, ShieldName);
        fork.StrategyIndex = StrategyIndex;

        Properties.CopyTo(fork.Properties);
        return fork;
    }

    private readonly struct PoolPolicy : IPooledObjectPolicy<KevlarContext>
    {
        public KevlarContext Create() => new();

        public bool TryReset(KevlarContext context)
        {
            context.CancellationToken = default;
            context.IsSynchronous = false;
            context.ShieldName = null;
            context.StrategyIndex = -1;
            context.TimeProvider = TimeProvider.System;
            context.Properties.Clear();
            return true;
        }
    }
}
