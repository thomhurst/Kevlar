using Microsoft.Extensions.Primitives;

namespace Kevlar.Extensions.DependencyInjection;

internal sealed class ReloadingShieldProvider : IShieldProvider, IDisposable
{
    private readonly object _reloadLock = new();
    private readonly Func<Shield> _factory;
    private readonly Action<Exception>? _onReloadFailure;
    private readonly IDisposable _subscription;
    private Shield _current;
    private bool _disposed;

    public ReloadingShieldProvider(
        Func<Shield> factory,
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

    public Shield Current => Volatile.Read(ref _current);

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
