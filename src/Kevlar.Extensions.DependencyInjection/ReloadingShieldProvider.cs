using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Kevlar.Extensions.DependencyInjection;

internal interface IReloadingProvider : IDisposable
{
    IReadOnlyList<Strategy> Strategies { get; }

    IReadOnlyList<ShieldRetirement> Retire();

    void SetLifecycleHandlers(
        Action<IReadOnlyList<ShieldRetirement>> retirementHandler,
        Action<Action> publicationGuard);
}

internal sealed class ReloadingShieldProvider(
    Func<Shield> factory,
    Func<IChangeToken> reloadTokenFactory,
    Action<Exception>? onReloadFailure,
    TimeSpan debounceDelay,
    TimeProvider timeProvider)
    : ReloadingProvider<Shield>(
        factory,
        reloadTokenFactory,
        onReloadFailure,
        debounceDelay,
        timeProvider),
        IShieldProvider
{
}

internal sealed class ReloadingShieldProvider<TResult>(
    Func<Shield<TResult>> factory,
    Func<IChangeToken> reloadTokenFactory,
    Action<Exception>? onReloadFailure,
    TimeSpan debounceDelay,
    TimeProvider timeProvider)
    : ReloadingProvider<Shield<TResult>>(
        factory,
        reloadTokenFactory,
        onReloadFailure,
        debounceDelay,
        timeProvider),
        IShieldProvider<TResult>
{
}

internal sealed class OptionsReloadingShieldProvider<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
    IOptionsMonitor<TOptions> monitor,
    string name,
    Func<TOptions, Shield> factory,
    Action<Exception>? onReloadFailure)
    : ReloadingProvider<Shield>(
        () => factory(monitor.Get(name)),
        reload => monitor.OnChange((_, changedName) =>
        {
            if (string.Equals(name, changedName, StringComparison.Ordinal))
            {
                reload();
            }
        }) ?? NullDisposable.Instance,
        onReloadFailure)
    , IShieldProvider
    where TOptions : class
{
}

internal sealed class OptionsReloadingShieldProvider<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions,
    TResult>(
    IOptionsMonitor<TOptions> monitor,
    string name,
    Func<TOptions, Shield<TResult>> factory,
    Action<Exception>? onReloadFailure)
    : ReloadingProvider<Shield<TResult>>(
        () => factory(monitor.Get(name)),
        reload => monitor.OnChange((_, changedName) =>
        {
            if (string.Equals(name, changedName, StringComparison.Ordinal))
            {
                reload();
            }
        }) ?? NullDisposable.Instance,
        onReloadFailure)
    , IShieldProvider<TResult>
    where TOptions : class
{
}

internal abstract class ReloadingProvider<TShield> : IReloadingProvider
    where TShield : class, IShieldLifecycle
{
    private readonly object _reloadLock = new();
    private readonly Func<TShield> _factory;
    private readonly Action<Exception>? _onReloadFailure;
    private readonly TimeSpan _debounceDelay;
    private readonly ITimer _reloadTimer;
    private readonly IDisposable? _subscription;
    private readonly List<ShieldRetirement> _retiredSnapshots = [];
    private readonly StrategyDisposalTracker _strategyDisposals = new();
    private Action<IReadOnlyList<ShieldRetirement>>? _retirementHandler;
    private Action<Action>? _publicationGuard;
    private TShield _current = null!;
    private bool _disposed;

    protected ReloadingProvider(
        Func<TShield> factory,
        Func<IChangeToken> reloadTokenFactory,
        Action<Exception>? onReloadFailure,
        TimeSpan debounceDelay,
        TimeProvider timeProvider)
        : this(
            factory,
            reload => ChangeToken.OnChange(reloadTokenFactory, reload),
            onReloadFailure,
            debounceDelay,
            timeProvider)
    {
    }

    protected ReloadingProvider(
        Func<TShield> factory,
        Func<Action, IDisposable> subscribe,
        Action<Exception>? onReloadFailure)
        : this(factory, subscribe, onReloadFailure, TimeSpan.Zero, TimeProvider.System)
    {
    }

    private ReloadingProvider(
        Func<TShield> factory,
        Func<Action, IDisposable> subscribe,
        Action<Exception>? onReloadFailure,
        TimeSpan debounceDelay,
        TimeProvider timeProvider)
    {
        _factory = factory;
        _onReloadFailure = onReloadFailure;
        _debounceDelay = debounceDelay;
        _reloadTimer = timeProvider.CreateTimer(
            static state => ((ReloadingProvider<TShield>)state!).Reload(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        try
        {
            _subscription = subscribe(ScheduleReload) ?? NullDisposable.Instance;
            lock (_reloadLock)
            {
                _current = factory();
                ShieldRetirement.Track(_current);
            }
        }
        catch
        {
            _subscription?.Dispose();
            _reloadTimer.Dispose();
            throw;
        }
    }

    public TShield Current => Volatile.Read(ref _current);

    IReadOnlyList<Strategy> IReloadingProvider.Strategies
    {
        get
        {
            lock (_reloadLock)
            {
                var seen = ShieldRetirement.CreateStrategySet();
                var strategies = new List<Strategy>();
                AddStrategiesForDisposal(((IShieldLifecycle)_current).Strategies, seen, strategies);
                foreach (var snapshot in _retiredSnapshots)
                {
                    AddStrategiesForDisposal(snapshot.Strategies, seen, strategies);
                }

                return strategies;
            }
        }
    }

    IReadOnlyList<ShieldRetirement> IReloadingProvider.Retire()
    {
        Dispose();
        lock (_reloadLock)
        {
            var retirements = new List<ShieldRetirement>(_retiredSnapshots.Count + 1);
            retirements.AddRange(_retiredSnapshots);
            retirements.Add(new ShieldRetirement(_current, _current));
            _retiredSnapshots.Clear();
            return retirements;
        }
    }

    void IReloadingProvider.SetLifecycleHandlers(
        Action<IReadOnlyList<ShieldRetirement>> retirementHandler,
        Action<Action> publicationGuard)
    {
        lock (_reloadLock)
        {
            _retirementHandler = retirementHandler;
            _publicationGuard = publicationGuard;
        }
    }

    public void Dispose()
    {
        lock (_reloadLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        try
        {
            _subscription?.Dispose();
        }
        finally
        {
            _reloadTimer.Dispose();
        }
    }

    private void ScheduleReload()
    {
        var reloadImmediately = false;
        lock (_reloadLock)
        {
            if (_disposed)
            {
                return;
            }

            if (_debounceDelay == TimeSpan.Zero)
            {
                reloadImmediately = true;
            }
            else
            {
                _ = _reloadTimer.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
            }
        }

        if (reloadImmediately)
        {
            Reload();
        }
    }

    private void Reload()
    {
        Exception? failure = null;
        List<ShieldRetirement>? reclaimable = null;

        void Publish()
        {
            lock (_reloadLock)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    var replacement = _factory();
                    ShieldRetirement.Track(replacement);
                    _retiredSnapshots.Add(new ShieldRetirement(_current, _current));
                    Volatile.Write(ref _current, replacement);
                    reclaimable = CollectReclaimableSnapshots();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }
        }

        Action<Action>? publicationGuard;
        lock (_reloadLock)
        {
            publicationGuard = _publicationGuard;
        }

        if (publicationGuard is null)
        {
            Publish();
        }
        else
        {
            publicationGuard(Publish);
        }

        Reclaim(reclaimable);

        ReportFailure(failure);
    }

    private List<ShieldRetirement>? CollectReclaimableSnapshots()
    {
        List<ShieldRetirement>? reclaimable = null;
        for (var index = _retiredSnapshots.Count - 1; index >= 0; index--)
        {
            var snapshot = _retiredSnapshots[index];
            if (!snapshot.CanReclaim())
            {
                continue;
            }

            reclaimable ??= [];
            reclaimable.Add(snapshot);
            _retiredSnapshots.RemoveAt(index);
        }

        return reclaimable;
    }

    private void Reclaim(List<ShieldRetirement>? reclaimable)
    {
        if (reclaimable is null)
        {
            return;
        }

        Action<IReadOnlyList<ShieldRetirement>>? retirementHandler;
        lock (_reloadLock)
        {
            retirementHandler = _retirementHandler;
        }

        if (retirementHandler is not null)
        {
            retirementHandler(reclaimable);
            return;
        }

        var retainedOrClaimed = ShieldRetirement.CreateStrategySet();
        retainedOrClaimed.UnionWith(((IShieldLifecycle)_current).Strategies);
        foreach (var snapshot in reclaimable)
        {
            snapshot.Reclaim(ReportFailure, retainedOrClaimed, _strategyDisposals);
        }
    }

    private void ReportFailure(Exception? failure)
    {
        if (failure is not null && _onReloadFailure is not null)
        {
            try
            {
                _onReloadFailure(failure);
            }
            catch
            {
                // A reporting failure must not disable future configuration reloads.
            }
        }
    }

    private static void AddStrategiesForDisposal(
        IReadOnlyList<Strategy> source,
        HashSet<Strategy> seen,
        List<Strategy> destination)
    {
        for (var index = source.Count - 1; index >= 0; index--)
        {
            if (seen.Add(source[index]))
            {
                destination.Add(source[index]);
            }
        }
    }
}

internal sealed class NullDisposable : IDisposable
{
    public static NullDisposable Instance { get; } = new();

    public void Dispose()
    {
    }
}
