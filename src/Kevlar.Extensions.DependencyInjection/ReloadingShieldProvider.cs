using Microsoft.Extensions.Primitives;

namespace Kevlar.Extensions.DependencyInjection;

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

internal abstract class ReloadingProvider<TShield> : IDisposable
    where TShield : class
{
    private readonly object _reloadLock = new();
    private readonly Func<TShield> _factory;
    private readonly Action<Exception>? _onReloadFailure;
    private readonly TimeSpan _debounceDelay;
    private readonly ITimer _reloadTimer;
    private readonly IDisposable? _subscription;
    private TShield _current = null!;
    private bool _disposed;

    protected ReloadingProvider(
        Func<TShield> factory,
        Func<IChangeToken> reloadTokenFactory,
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
            _subscription = ChangeToken.OnChange(reloadTokenFactory, ScheduleReload);
            lock (_reloadLock)
            {
                _current = factory();
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
