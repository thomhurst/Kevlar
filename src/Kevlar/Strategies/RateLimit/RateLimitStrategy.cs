using Kevlar.Internal;

namespace Kevlar.Strategies;

/// <summary>
/// Token bucket with reservations: the bucket refills continuously at Permits/Window. When empty,
/// up to QueueLimit executions may reserve future tokens (driving the balance negative) and wait
/// for their replenishment time; beyond that, executions are rejected immediately.
/// </summary>
internal sealed class RateLimitStrategy : Strategy
{
    private readonly object _gate = new();
    private readonly double _permitsPerSecond;
    private readonly double _burst;
    private readonly int _queueLimit;

    private double _tokens;
    private long _lastRefillTimestamp = -1;

    public RateLimitStrategy(RateLimitOptions options)
    {
        Throw.IfOutOfRange(options.Permits <= 0, nameof(options), "Permits must be positive.");
        Throw.IfOutOfRange(options.Window <= TimeSpan.Zero, nameof(options), "Window must be positive.");
        Throw.IfOutOfRange(options.Burst is <= 0, nameof(options), "Burst must be positive.");
        Throw.IfOutOfRange(options.QueueLimit < 0, nameof(options), "QueueLimit must not be negative.");

        _permitsPerSecond = options.Permits / options.Window.TotalSeconds;
        _burst = options.Burst ?? options.Permits;
        _queueLimit = options.QueueLimit;
        _tokens = _burst;
    }

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (!TryAcquire(context.TimeProvider, out var wait, out var retryAfter))
        {
            return Outcome<T>.FromException(new RateLimitExceededException(retryAfter));
        }

        if (wait > TimeSpan.Zero)
        {
            try
            {
                await DelayHelper.DelayAsync(context, wait).ConfigureAwait(false);
            }
            catch (OperationCanceledException cancelled)
            {
                return Outcome<T>.FromException(cancelled);
            }
        }

        return await next.InvokeAsync(context).ConfigureAwait(false);
    }

    private bool TryAcquire(TimeProvider timeProvider, out TimeSpan wait, out TimeSpan? retryAfter)
    {
        lock (_gate)
        {
            var now = timeProvider.GetTimestamp();

            if (_lastRefillTimestamp >= 0)
            {
                var elapsed = timeProvider.GetElapsedTime(_lastRefillTimestamp, now).TotalSeconds;
                if (elapsed > 0)
                {
                    _tokens = Math.Min(_burst, _tokens + (elapsed * _permitsPerSecond));
                }
            }

            _lastRefillTimestamp = now;

            var balanceAfterTake = _tokens - 1;

            if (balanceAfterTake >= 0)
            {
                _tokens = balanceAfterTake;
                wait = TimeSpan.Zero;
                retryAfter = null;
                return true;
            }

            if (-balanceAfterTake <= _queueLimit)
            {
                _tokens = balanceAfterTake;
                wait = TimeSpan.FromSeconds(-balanceAfterTake / _permitsPerSecond);
                retryAfter = null;
                return true;
            }

            wait = TimeSpan.Zero;
            retryAfter = TimeSpan.FromSeconds((1 - _tokens) / _permitsPerSecond);
            return false;
        }
    }
}
