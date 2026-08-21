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
        var timer = new ControlledTimer(callback, state, dueTime, period);
        lock (_sync)
        {
            _timers.Add(timer);
        }

        _createdTimers.Signal();
        return timer;
    }

    public Task<int> WaitForTimersAsync(int count) => _createdTimers.WaitForAsync(count);

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

    private sealed class ControlledTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period) : ITimer
    {
        private readonly object _sync = new();
        private int _disposed;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public TimeSpan DueTime { get; private set; } = dueTime;

        public TimeSpan Period { get; private set; } = period;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_sync)
            {
                if (IsDisposed)
                {
                    return false;
                }

                DueTime = dueTime;
                Period = period;
                return true;
            }
        }

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
            callback(state);
        }

        public void FireCaptured() => callback(state);

        public void EnsureActive()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(ControlledTimer));
            }
        }
    }
}
