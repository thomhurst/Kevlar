using System.Diagnostics;
using Reservoir;

namespace Kevlar.Internal;

/// <summary>Reuses timeout timers without rescheduling later deadlines on every rental.</summary>
internal static class TimeoutSourcePool
{
#if NET8_0_OR_GREATER
    // The thread-static slot is the steady-state path: one static field access instead of the
    // pool's per-instance thread-local lookup on both rent and return. The shared pool absorbs
    // sources returned while the slot is occupied and serves threads whose slot is empty.
    [ThreadStatic]
    private static Source? t_cached;

    private static readonly ObjectPool<Source, Policy> Pool = new(
        default,
        ObjectPool<Source, Policy>.DefaultMaximumRetained,
        threadLocalFastPath: false);
#endif

    public static CancellationTokenSource RentLinked(CancellationToken upstreamToken)
    {
#if NET8_0_OR_GREATER
        var source = t_cached;
        if (source is not null)
        {
            t_cached = null;
        }
        else
        {
            source = Pool.Rent();
        }

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

    /// <summary>
    /// Ends a rental from <see cref="RentLinked"/>, or disposes any other source. Pooled sources skip
    /// the public <see cref="CancellationTokenSource.Dispose()"/> indirection.
    /// </summary>
    public static void Return(CancellationTokenSource source)
    {
#if NET8_0_OR_GREATER
        if (source is Source pooled)
        {
            pooled.Release();
            return;
        }
#endif
        source.Dispose();
    }

#if NET8_0_OR_GREATER
    private sealed class Source : CancellationTokenSource
    {
        // TimeProvider.System timestamps are Stopwatch timestamps. Deadlines stay in that unit so
        // arming compares integers instead of converting elapsed time on every rental.
        private static readonly double TimestampTicksPerTimeSpanTick =
            (double)Stopwatch.Frequency / TimeSpan.TicksPerSecond;

        // _state packs a rental generation (upper bits) with the rental kind (lower bits). Arm and
        // Release are the per-call path and use one interlocked operation each instead of the gate;
        // the generation stops a timer callback that read an older rental's deadline from selecting
        // cancellation for a newer rental of the same source.
        private const long KindMask = 3;
        private const long Idle = 0;
        private const long Armed = 1;
        // Expiry selected cancellation. The source is never reused once it reaches this kind.
        private const long Firing = 2;
        private const long GenerationIncrement = 4;
        // No timer wakeup is pending.
        private const long NotScheduled = long.MaxValue;

        // Serializes timer scheduling, timer callbacks, and destruction; never taken on the hot path.
        private readonly Lock _gate = new();
        private readonly Timer _timer;
        private CancellationTokenRegistration _upstream;
        private long _state;
        private long _startedAt;
        private long _deadline;
        private long _scheduledDue = NotScheduled;
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

        private static long ToTimestampTicks(TimeSpan duration) =>
            Stopwatch.Frequency == TimeSpan.TicksPerSecond
                ? duration.Ticks
                : (long)(duration.Ticks * TimestampTicksPerTimeSpanTick);

        private static TimeSpan FromTimestampTicks(long timestampTicks) =>
            Stopwatch.Frequency == TimeSpan.TicksPerSecond
                ? new TimeSpan(timestampTicks)
                // Round up so a wakeup never lands before the deadline it was scheduled for.
                : new TimeSpan((long)Math.Ceiling(timestampTicks / TimestampTicksPerTimeSpanTick));

        public void Arm(TimeSpan timeout)
        {
            var now = Stopwatch.GetTimestamp();
            var deadline = now + ToTimestampTicks(timeout);
            _startedAt = now;
            _deadline = deadline;

            // Only the renting thread arms, and only from Idle. The full fence publishes the deadline
            // before the new generation and orders this store before the _scheduledDue load below;
            // OnTimer clears _scheduledDue before loading _state, so at least one side observes the
            // other and this rental always has a wakeup at or before its deadline.
            var state = Volatile.Read(ref _state);
            Interlocked.Exchange(ref _state, (state & ~KindMask) + GenerationIncrement + Armed);

            // Keep an earlier pending wakeup; it re-checks the current deadline when it fires.
            if (deadline < Volatile.Read(ref _scheduledDue))
            {
                ScheduleIfEarlier(timeout, deadline);
            }
        }

        private void ScheduleIfEarlier(TimeSpan delay, long due)
        {
            lock (_gate)
            {
                if (!_destroyed && due < _scheduledDue)
                {
                    Schedule(delay, due);
                }
            }
        }

        private void Schedule(TimeSpan delay, long due)
        {
            Volatile.Write(ref _scheduledDue, due);
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

                Volatile.Write(ref _scheduledDue, NotScheduled);
                Interlocked.MemoryBarrier();
                while (true)
                {
                    var state = Volatile.Read(ref _state);
                    if ((state & KindMask) != Armed)
                    {
                        return;
                    }

                    var deadline = Volatile.Read(ref _deadline);
                    var remaining = deadline - Stopwatch.GetTimestamp();
                    if (remaining > 0)
                    {
                        // A deadline read from a newer rental is still a valid wakeup; one from an
                        // older rental is at worst an early wakeup that re-checks.
                        Schedule(FromTimestampTicks(remaining), deadline);
                        return;
                    }

                    // Select cancellation only if the rental whose deadline expired is still armed.
                    if (Interlocked.CompareExchange(ref _state, (state & ~KindMask) | Firing, state) == state)
                    {
                        break;
                    }
                }
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Release();
            }
        }

        public void Release()
        {
            // Drain the previous upstream callback before publishing the source for reuse.
            _upstream.Dispose();
            _upstream = default;

            var state = Volatile.Read(ref _state);
            if ((state & KindMask) == Armed)
            {
                // Losing this race means expiry selected cancellation; ResetForReuse then refuses reuse.
                Interlocked.CompareExchange(ref _state, state & ~KindMask, state);
            }

            if (t_cached is null && ResetForReuse())
            {
                t_cached = this;
                return;
            }
            Pool.Return(this);
        }

        // An idle rental can no longer be cancelled by its timer, and upstream callbacks were drained.
        public bool ResetForReuse() => (Volatile.Read(ref _state) & KindMask) == Idle && TryReset();

        public void Destroy()
        {
            _upstream.Dispose();
            _upstream = default;
            lock (_gate)
            {
                _destroyed = true;
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
