using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Kevlar.Extensions.DependencyInjection;

internal interface IReloadingProvider : IDisposable
{
    IReadOnlyList<IShieldLifecycle> Snapshots { get; }
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
        })!,
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
        })!,
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
    private readonly List<TShield> _snapshots = [];
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
            _subscription = subscribe(ScheduleReload);
            lock (_reloadLock)
            {
                _current = factory();
                _snapshots.Add(_current);
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

    IReadOnlyList<IShieldLifecycle> IReloadingProvider.Snapshots
    {
        get
        {
            lock (_reloadLock)
            {
                return _snapshots.Cast<IShieldLifecycle>().ToArray();
            }
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

        lock (_reloadLock)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var replacement = _factory();
                _snapshots.Add(replacement);
                Volatile.Write(ref _current, replacement);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

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
}
