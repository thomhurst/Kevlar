namespace Kevlar.Tests;

internal sealed class ControlledTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<ControlledTimer> _timers = [];
    private readonly AsyncCounter _createdTimers = new("controlled timers created");
    private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;
    private long _timestamp;
    private int _queuedCallbackCount;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public int TimerCount
    {
        get
        {
            lock (_sync)
            {
                return _timers.Count;
            }
        }
    }

    public int QueuedCallbackCount => Volatile.Read(ref _queuedCallbackCount);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _utcNow;
        }
    }

    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ControlledTimer(this, callback, state, dueTime, period);
        lock (_sync)
        {
            _timers.Add(timer);
        }

        _createdTimers.Signal();
        return timer;
    }

    public Task<int> WaitForTimersAsync(int count, TimeSpan? timeout = null) =>
        _createdTimers.WaitForAsync(count, timeout);

    /// <summary>
    /// Fires every undisposed timer whose due time has been reached, whatever order the timers were created in.
    /// </summary>
    /// <returns>The number of timer callbacks invoked.</returns>
    public int FireDueTimers()
    {
        ControlledTimer[] timers;
        long now;
        lock (_sync)
        {
            timers = [.. _timers];
            now = _timestamp;
        }

        var fired = 0;
        foreach (var timer in timers)
        {
            if (timer.TryClaimDue(now))
            {
                timer.FireCaptured();
                fired++;
            }
        }

        return fired;
    }

    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        lock (_sync)
        {
            _utcNow += elapsed;
            _timestamp = checked(_timestamp + elapsed.Ticks);
        }
    }

    public void FireTimer(int timerIndex) => GetTimer(timerIndex).Fire();

    public QueuedTimerCallback QueueTimerCallback(int timerIndex)
    {
        var timer = GetTimer(timerIndex);
        timer.EnsureActive();
        Interlocked.Increment(ref _queuedCallbackCount);
        return new QueuedTimerCallback(timer.FireCaptured, OnQueuedCallbackCompleted);
    }

    public bool IsTimerDisposed(int timerIndex) => GetTimer(timerIndex).IsDisposed;

    public TimeSpan GetTimerDueTime(int timerIndex) => GetTimer(timerIndex).DueTime;

    private ControlledTimer GetTimer(int timerIndex)
    {
        lock (_sync)
        {
            return _timers[timerIndex];
        }
    }

    private void OnQueuedCallbackCompleted() => Interlocked.Decrement(ref _queuedCallbackCount);

    internal sealed class QueuedTimerCallback(Action callback, Action completed)
    {
        private int _fired;

        public bool IsPending => Volatile.Read(ref _fired) == 0;

        public void Fire()
        {
            if (Interlocked.Exchange(ref _fired, 1) != 0)
            {
                throw new InvalidOperationException("The queued timer callback has already fired.");
            }

            try
            {
                callback();
            }
            finally
            {
                completed();
            }
        }
    }

    private sealed class ControlledTimer : ITimer
    {
        private readonly object _sync = new();
        private readonly ControlledTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private long _dueTimestamp;
        private int _disposed;

        public ControlledTimer(
            ControlledTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            Schedule(dueTime, period);
        }

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public TimeSpan DueTime { get; private set; }

        public TimeSpan Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_sync)
            {
                if (IsDisposed)
                {
                    return false;
                }

                Schedule(dueTime, period);
                return true;
            }
        }

        // Claims one due firing at the provider timestamp `now`. One-shot timers fire once; periodic timers re-arm.
        public bool TryClaimDue(long now)
        {
            lock (_sync)
            {
                if (IsDisposed || _dueTimestamp > now)
                {
                    return false;
                }

                _dueTimestamp = IsUnscheduled(Period) ? long.MaxValue : _dueTimestamp + Period.Ticks;
                return true;
            }
        }

        private void Schedule(TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime;
            Period = period;
            // Provider timestamps are in TimeSpan ticks (TimestampFrequency is TicksPerSecond).
            _dueTimestamp = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : _owner.GetTimestamp() + dueTime.Ticks;
        }

        private static bool IsUnscheduled(TimeSpan period) => period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero;

        public void Dispose()
        {
            lock (_sync)
            {
                Interlocked.Exchange(ref _disposed, 1);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        public void Fire()
        {
            EnsureActive();
            _callback(_state);
        }

        public void FireCaptured() => _callback(_state);

        public void EnsureActive()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(ControlledTimer));
            }
        }
    }
}
