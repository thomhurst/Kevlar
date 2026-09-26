using Reservoir;

namespace Kevlar.Internal;

/// <summary>Reuses timeout timers without rescheduling later deadlines on every rental.</summary>
internal static class TimeoutSourcePool
{
#if NET8_0_OR_GREATER
    private static readonly ObjectPool<Source, Policy> Pool = new(
        default,
        ObjectPool<Source, Policy>.DefaultMaximumRetained,
        threadLocalFastPath: true);
#endif

    public static CancellationTokenSource RentLinked(CancellationToken upstreamToken)
    {
#if NET8_0_OR_GREATER
        var source = Pool.Rent();
        try
        {
            source.Link(upstreamToken);
            return source;
        }
        catch
        {
            source.Destroy();
            throw;
        }
#else
        return CancellationTokenSourcePool.Shared.RentLinked(upstreamToken);
#endif
    }

    public static void Arm(CancellationTokenSource source, TimeSpan timeout)
    {
#if NET8_0_OR_GREATER
        ((Source)source).Arm(timeout);
#else
        source.CancelAfter(timeout);
#endif
    }

    public static long ArmAndGetTimestamp(CancellationTokenSource source, TimeSpan timeout)
    {
#if NET8_0_OR_GREATER
        var pooled = (Source)source;
        pooled.Arm(timeout);
        // This rental owns the source until completion; arming already sampled the clock.
        return pooled.StartedAt;
#else
        var startedAt = TimeProvider.System.GetTimestamp();
        source.CancelAfter(timeout);
        return startedAt;
#endif
    }

#if NET8_0_OR_GREATER
    private sealed class Source : CancellationTokenSource
    {
        private readonly object _gate = new();
        private readonly Timer _timer;
        private CancellationTokenRegistration _upstream;
        private long _startedAt;
        private TimeSpan _timeout;
        private long _scheduledAt;
        private TimeSpan _scheduledDelay;
        private bool _scheduled;
        private bool _active;
        private bool _cancellationInFlight;
        private bool _destroyed;

        public Source()
        {
            // Pooled timers must not retain a caller's AsyncLocal values.
            if (ExecutionContext.IsFlowSuppressed())
            {
                _timer = CreateTimer(this);
            }
            else
            {
                using var suppression = ExecutionContext.SuppressFlow();
                _timer = CreateTimer(this);
            }
        }

        private static Timer CreateTimer(Source source) => new(
            static state => ((Source)state!).OnTimer(),
            source,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        public void Link(CancellationToken token)
        {
            if (token.CanBeCanceled)
            {
                _upstream = token.UnsafeRegister(static state => ((Source)state!).Cancel(), this);
            }
        }

        public long StartedAt => _startedAt;

        public void Arm(TimeSpan timeout)
        {
            lock (_gate)
            {
                _startedAt = TimeProvider.System.GetTimestamp();
                _timeout = timeout;
                _active = true;
                if (!_scheduled || timeout < _scheduledDelay - TimeProvider.System.GetElapsedTime(_scheduledAt, _startedAt))
                {
                    Schedule(timeout, _startedAt);
                }
            }
        }

        private void Schedule(TimeSpan delay, long now)
        {
            _scheduledAt = now;
            _scheduledDelay = delay;
            _scheduled = true;
            _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }

        private void OnTimer()
        {
            lock (_gate)
            {
                if (_destroyed)
                {
                    return;
                }
                _scheduled = false;
                if (!_active)
                {
                    return;
                }
                var now = TimeProvider.System.GetTimestamp();
                var remaining = _timeout - TimeProvider.System.GetElapsedTime(_startedAt, now);
                if (remaining > TimeSpan.Zero)
                {
                    Schedule(remaining, now);
                    return;
                }
                _active = false;
                _cancellationInFlight = true;
            }

            // Never hold the source gate across user cancellation callbacks. Completion
            // can return this rental from a callback or another thread while Cancel runs.
            try
            {
                Cancel();
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _destroyed))
            {
                // Completion destroyed this source after expiry selected cancellation.
            }
            finally
            {
                lock (_gate)
                {
                    _cancellationInFlight = false;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }
            // Drain the previous upstream callback before publishing the source to the pool.
            _upstream.Dispose();
            _upstream = default;
            lock (_gate)
            {
                _active = false;
                Pool.Return(this);
            }
        }

        public bool ResetForReuse() => !_cancellationInFlight && TryReset();

        public void Destroy()
        {
            _upstream.Dispose();
            _upstream = default;
            lock (_gate)
            {
                _destroyed = true;
                _active = false;
                _timer.Dispose();
                base.Dispose(disposing: true);
            }
        }
    }

    private readonly struct Policy : IPooledObjectPolicy<Source>
    {
        public Source Create() => new();
        public bool TryReset(Source source) => source.ResetForReuse();
        public void Destroy(Source source) => source.Destroy();
    }
#endif
}
