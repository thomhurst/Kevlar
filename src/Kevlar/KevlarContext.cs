using System.Collections.Concurrent;

namespace Kevlar;

/// <summary>
/// Ambient state for a single execution flowing through a policy pipeline.
/// Contexts are created and pooled by Kevlar automatically; user code observes them
/// in strategy callbacks and never needs to construct or return them.
/// </summary>
public sealed class KevlarContext
{
    private const int MaxPooled = 128;

    private static readonly ConcurrentQueue<KevlarContext> Pool = new();
    private static int _pooledCount;

    private readonly bool _poolable;

    private KevlarContext(bool poolable) => _poolable = poolable;

    /// <summary>
    /// The cancellation token for the current scope. Strategies such as timeouts replace this
    /// token for the layers beneath them, which is why executed delegates must use the token
    /// they are handed rather than a captured one.
    /// </summary>
    public CancellationToken CancellationToken { get; internal set; }

    /// <summary><see langword="true"/> when the execution was started through a synchronous <c>Execute</c> call.</summary>
    public bool IsSynchronous { get; internal set; }

    /// <summary>The name of the executing policy, if one was assigned via <c>WithName</c>.</summary>
    public string? PolicyName { get; internal set; }

    /// <summary>The time provider used for delays, timeouts and time-window calculations.</summary>
    public TimeProvider TimeProvider { get; internal set; } = TimeProvider.System;

    /// <summary>Custom properties carried through the execution.</summary>
    public KevlarProperties Properties { get; } = new();

    internal static KevlarContext Rent(CancellationToken cancellationToken, bool isSynchronous, TimeProvider timeProvider, string? policyName)
    {
        if (Pool.TryDequeue(out var context))
        {
            Interlocked.Decrement(ref _pooledCount);
        }
        else
        {
            context = new KevlarContext(poolable: true);
        }

        context.CancellationToken = cancellationToken;
        context.IsSynchronous = isSynchronous;
        context.TimeProvider = timeProvider;
        context.PolicyName = policyName;
        return context;
    }

    internal static void Return(KevlarContext context)
    {
        if (!context._poolable)
        {
            return;
        }

        context.CancellationToken = default;
        context.PolicyName = null;
        context.TimeProvider = TimeProvider.System;
        context.Properties.Clear();

        if (Interlocked.Increment(ref _pooledCount) <= MaxPooled)
        {
            Pool.Enqueue(context);
        }
        else
        {
            Interlocked.Decrement(ref _pooledCount);
        }
    }

    /// <summary>
    /// Creates a detached copy of this context for a concurrent attempt (used by hedging).
    /// The copy shares no mutable state with the original.
    /// </summary>
    internal KevlarContext Fork(CancellationToken cancellationToken)
    {
        var fork = new KevlarContext(poolable: false)
        {
            CancellationToken = cancellationToken,
            IsSynchronous = IsSynchronous,
            PolicyName = PolicyName,
            TimeProvider = TimeProvider,
        };

        Properties.CopyTo(fork.Properties);
        return fork;
    }
}
