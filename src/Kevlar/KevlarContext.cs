using System.Diagnostics;
using Reservoir;

namespace Kevlar;

/// <summary>
/// Ambient state for a single execution flowing through a shield pipeline.
/// Contexts are created and pooled by Kevlar automatically; user code observes them
/// in strategy callbacks or context-aware execution delegates and never needs to construct
/// or return them. A context is valid only during the current callback or delegate invocation
/// and must never be retained.
/// </summary>
/// <remarks>
/// Debug builds of Kevlar enforce that contract: a context that has gone back to the pool throws
/// <see cref="InvalidOperationException"/> from every public member until it is handed out again.
/// Release builds carry no such check, so a retained context there silently observes another
/// execution's state.
/// </remarks>
public sealed class KevlarContext
{
    internal const int PoolCapacity = 128;

    private static readonly ObjectPool<KevlarContext, PoolPolicy> Pool = new(maxCapacity: PoolCapacity);

    private readonly KevlarProperties _properties = new();
    private KevlarProperties? _completionProperties;
    private readonly object _completionPropertiesLock = new();
    private bool _hasCompletionProperties;

    private CancellationToken _cancellationToken;
    private long _activeStrategyMask;
    private bool _isSynchronous;
    private string? _shieldName;
    private int _strategyIndex = -1;
    private TimeProvider _timeProvider = TimeProvider.System;

#if DEBUG
    private bool _returnedToPool;
#endif

    private KevlarContext()
    {
    }

    /// <summary>
    /// The cancellation token for the current scope. Strategies such as timeouts replace this
    /// token for the layers beneath them, which is why executed delegates must use the token
    /// they are handed rather than a captured one.
    /// </summary>
    public CancellationToken CancellationToken
    {
        get
        {
            ThrowIfReturnedToPool();
            return _cancellationToken;
        }

        internal set => _cancellationToken = value;
    }

    /// <summary><see langword="true"/> when the execution was started through a synchronous <c>Execute</c> call.</summary>
    public bool IsSynchronous
    {
        get
        {
            ThrowIfReturnedToPool();
            return _isSynchronous;
        }

        internal set => _isSynchronous = value;
    }

    /// <summary>The name of the executing shield, if one was assigned via <c>WithName</c>.</summary>
    public string? ShieldName
    {
        get
        {
            ThrowIfReturnedToPool();
            return _shieldName;
        }

        internal set => _shieldName = value;
    }

    /// <summary>
    /// The zero-based position of the strategy currently executing, or <c>-1</c> outside a
    /// strategy. Nested strategies temporarily replace this value and restore it on return.
    /// </summary>
    public int StrategyIndex
    {
        get
        {
            ThrowIfReturnedToPool();
            return _strategyIndex;
        }

        internal set => _strategyIndex = value;
    }

    /// <summary>The time provider used for delays, timeouts and time-window calculations.</summary>
    public TimeProvider TimeProvider
    {
        get
        {
            ThrowIfReturnedToPool();
            return _timeProvider;
        }

        internal set => _timeProvider = value;
    }

    /// <summary>
    /// Custom properties carried through the execution. The bag is pooled with this context
    /// and must not be retained beyond the current callback or delegate invocation.
    /// </summary>
    public KevlarProperties Properties
    {
        get
        {
            ThrowIfReturnedToPool();
            return _properties;
        }
    }

    internal KevlarProperties PropertiesForCompletion =>
        _hasCompletionProperties ? _completionProperties! : _properties;

    internal void CaptureCompletionProperties(
        KevlarProperties properties,
        KevlarProperties? baseline = null)
    {
        lock (_completionPropertiesLock)
        {
            _completionProperties ??= new KevlarProperties();
            _completionProperties.Clear();
            properties.CopyTo(_completionProperties);
            if (baseline is not null)
            {
                _properties.ApplyChangesSince(baseline, _completionProperties);
            }

            _properties.MirrorMutationsTo(_completionProperties);
            _hasCompletionProperties = true;
        }
    }

    internal void CopyCompletionPropertiesTo(
        KevlarContext target,
        KevlarProperties? baseline = null)
    {
        lock (_completionPropertiesLock)
        {
            target.CaptureCompletionProperties(PropertiesForCompletion, baseline);
        }
    }

    internal static KevlarContext Rent(CancellationToken cancellationToken, bool isSynchronous, TimeProvider timeProvider, string? shieldName)
    {
        var context = Pool.Rent();

        MarkRented(context);
        context.CancellationToken = cancellationToken;
        context.IsSynchronous = isSynchronous;
        context.TimeProvider = timeProvider;
        context.ShieldName = shieldName;
        context.StrategyIndex = -1;
        return context;
    }

    internal static void Return(KevlarContext context)
    {
        MarkReturned(context);
        Pool.Return(context);
    }

    /// <summary>Creates the detached context used by manual circuit-breaker transitions.</summary>
    internal static KevlarContext CreateManual() => new();

    /// <summary>Creates a detached snapshot for an event that may outlive this pooled context.</summary>
    internal KevlarContext CreateDetachedSnapshot()
    {
        var snapshot = new KevlarContext
        {
            CancellationToken = CancellationToken,
            IsSynchronous = IsSynchronous,
            TimeProvider = TimeProvider,
            ShieldName = ShieldName,
            StrategyIndex = StrategyIndex,
        };
        Properties.CopyTo(snapshot.Properties);
        return snapshot;
    }

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

    internal bool TryEnterStrategy(int strategyIndex)
    {
        var bit = 1L << (strategyIndex & 63);
        while (true)
        {
            var current = Volatile.Read(ref _activeStrategyMask);
            if ((current & bit) != 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _activeStrategyMask, current | bit, current) == current)
            {
                return true;
            }
        }
    }

    internal void ExitStrategy(int strategyIndex)
    {
        var bit = ~(1L << (strategyIndex & 63));
        while (true)
        {
            var current = Volatile.Read(ref _activeStrategyMask);
            if (Interlocked.CompareExchange(ref _activeStrategyMask, current & bit, current) == current)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Clears the debug pooling guard on a context that Kevlar's own pool tests inspect after it
    /// went back to the pool. A no-op in release builds, where the guard does not exist.
    /// </summary>
    internal static void AllowPooledInspection(KevlarContext context) => MarkRented(context);

    [Conditional("DEBUG")]
    private static void MarkRented(KevlarContext context)
    {
#if DEBUG
        context._returnedToPool = false;
        context._properties.MarkRented();
        context._completionProperties?.MarkRented();
#endif
    }

    [Conditional("DEBUG")]
    private static void MarkReturned(KevlarContext context)
    {
#if DEBUG
        context._returnedToPool = true;
        context._properties.MarkReturned();
        context._completionProperties?.MarkReturned();
#endif
    }

    [Conditional("DEBUG")]
    private void ThrowIfReturnedToPool()
    {
#if DEBUG
        if (_returnedToPool)
        {
            throw new InvalidOperationException(
                "This KevlarContext has been returned to Kevlar's context pool and is no longer valid. " +
                "A context — and its Properties — may only be used while the callback or delegate that " +
                "received it is running; never retain it afterwards. Copy the values you need instead.");
        }
#endif
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
            context._activeStrategyMask = 0;
            context.TimeProvider = TimeProvider.System;
            context._properties.MirrorMutationsTo(null);
            context._properties.Clear();
            context._completionProperties?.Clear();
            context._hasCompletionProperties = false;
            return true;
        }
    }
}
