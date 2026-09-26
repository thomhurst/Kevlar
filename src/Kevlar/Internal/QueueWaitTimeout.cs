namespace Kevlar.Internal;

/// <summary>Owns cancellation only while an execution waits for admission.</summary>
internal sealed class QueueWaitTimeout : IDisposable
{
    private readonly CancellationTokenSource _source;
    private readonly ITimer _timer;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;
    private readonly long _startedAt;
    private readonly CancellationToken _callerToken;

    public QueueWaitTimeout(KevlarContext context, TimeSpan timeout)
    {
        _timeProvider = context.TimeProvider;
        _timeout = timeout;
        _startedAt = _timeProvider.GetTimestamp();
        _callerToken = context.CancellationToken;
        _source = CancellationTokenSource.CreateLinkedTokenSource(_callerToken);
        try
        {
            _timer = _timeProvider.CreateTimer(static state =>
            {
                try
                {
                    ((CancellationTokenSource)state!).Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // A timer callback already dispatched before disposal may finish afterward.
                }
            }, _source, timeout, Timeout.InfiniteTimeSpan);
        }
        catch
        {
            _source.Dispose();
            throw;
        }
    }

    public CancellationToken Token => _source.Token;

    public void ThrowIfCancellationRequested()
    {
        _callerToken.ThrowIfCancellationRequested();
        // Enforce the deadline even when timer dispatch is delayed.
        if (_timeProvider.GetElapsedTime(_startedAt) >= _timeout)
        {
            _source.Cancel();
        }
        Token.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _source.Dispose();
    }
}
