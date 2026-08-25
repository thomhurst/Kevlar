using Microsoft.Extensions.Primitives;

namespace Kevlar.Extensions.DependencyInjection;

internal sealed class ReloadingShieldProvider(
    Func<Shield> factory,
    Func<IChangeToken> reloadTokenFactory,
    Action<Exception>? onReloadFailure)
    : ReloadingProvider<Shield>(factory, reloadTokenFactory, onReloadFailure), IShieldProvider
{
}

internal sealed class ReloadingShieldProvider<TResult>(
    Func<Shield<TResult>> factory,
    Func<IChangeToken> reloadTokenFactory,
    Action<Exception>? onReloadFailure)
    : ReloadingProvider<Shield<TResult>>(factory, reloadTokenFactory, onReloadFailure),
        IShieldProvider<TResult>
{
}

internal abstract class ReloadingProvider<TShield> : IDisposable
    where TShield : class
{
    private readonly object _reloadLock = new();
    private readonly Func<TShield> _factory;
    private readonly Action<Exception>? _onReloadFailure;
    private readonly IDisposable _subscription;
    private TShield _current = null!;
    private bool _disposed;

    protected ReloadingProvider(
        Func<TShield> factory,
        Func<IChangeToken> reloadTokenFactory,
        Action<Exception>? onReloadFailure)
    {
        _factory = factory;
        _onReloadFailure = onReloadFailure;
        _subscription = ChangeToken.OnChange(reloadTokenFactory, Reload);

        try
        {
            lock (_reloadLock)
            {
                _current = factory();
            }
        }
        catch
        {
            _subscription.Dispose();
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

        _subscription.Dispose();
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
