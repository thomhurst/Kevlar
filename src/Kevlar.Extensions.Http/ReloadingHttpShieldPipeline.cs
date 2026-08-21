using Microsoft.Extensions.Primitives;

namespace Kevlar.Extensions.Http;

internal sealed class ReloadingHttpShieldPipeline : IDisposable
{
    private readonly object _reloadLock = new();
    private readonly Func<HttpShieldPipeline> _factory;
    private readonly Action<Exception>? _onReloadFailure;
    private readonly IDisposable _subscription;
    private HttpShieldPipeline _current;
    private bool _disposed;

    public ReloadingHttpShieldPipeline(
        Func<HttpShieldPipeline> factory,
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

    public HttpShieldPipeline Current => Volatile.Read(ref _current);

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
                Volatile.Write(ref _current, _factory());
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
                // Reporting failures must not disable later reloads.
            }
        }
    }
}
