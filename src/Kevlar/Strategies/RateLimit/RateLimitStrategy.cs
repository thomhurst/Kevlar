using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kevlar.Internal;

namespace Kevlar.Strategies;

/// <summary>
/// Token bucket with reservations: the bucket refills continuously at Permits/Window. When empty,
/// up to QueueLimit executions may reserve future tokens (driving the balance negative) and wait
/// for their replenishment time; beyond that, executions are rejected immediately.
/// Uses GCRA's atomic theoretical-arrival schedule, which is equivalent to a token bucket.
/// </summary>
internal sealed class RateLimitStrategy : Strategy
{
    private static readonly double SecondsPerSystemTimestamp = 1d / Stopwatch.Frequency;

    private readonly long _systemTimestampOrigin = Stopwatch.GetTimestamp();
    private readonly ConditionalWeakTable<TimeProvider, CustomTimestampOrigin> _customTimestampOrigins = new();
    private readonly int _permits;
    private readonly TimeSpan _window;
    private readonly int _burst;
    private readonly int _queueLimit;
    private readonly double _timestampUnitsPerPermit;
    private readonly double _burstTolerance;
    private readonly object _queueGate = new();

    private double _theoreticalArrival = double.NegativeInfinity;
    private Reservation? _queueHead;
    private Reservation? _queueTail;
    private int _queuedReservations;

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    public RateLimitStrategy(RateLimitOptions options)
    {
        Throw.IfOutOfRange(options.Permits <= 0, nameof(options), "Permits must be positive.");
        Throw.IfOutOfRange(options.Window <= TimeSpan.Zero, nameof(options), "Window must be positive.");
        Throw.IfOutOfRange(options.Burst is <= 0, nameof(options), "Burst must be positive.");
        Throw.IfOutOfRange(options.QueueLimit < 0, nameof(options), "QueueLimit must not be negative.");

        _permits = options.Permits;
        _window = options.Window;
        _burst = options.Burst ?? options.Permits;
        _queueLimit = options.QueueLimit;
        _timestampUnitsPerPermit = options.Window.TotalSeconds * Stopwatch.Frequency / options.Permits;
        _burstTolerance = (_burst - 1) * _timestampUnitsPerPermit;
    }

    public override string Describe()
    {
        var queue = _queueLimit > 0 ? $", queue {_queueLimit}" : string.Empty;
        var burst = _burst != _permits ? $", burst {_burst}" : string.Empty;
        return $"RateLimit({_permits}/{DescribeHelper.Time(_window)}{burst}{queue})";
    }

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (!TryAcquire(context.TimeProvider, out var reservation, out var retryAfter))
        {
            KevlarMetrics.Rejection(context.ShieldName, "rate_limit");
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(new RateLimitExceededException(retryAfter)));
        }

        return reservation is null
            ? next.InvokeAsync(context)
            : ExecuteReservedAsync(next, context, reservation);
    }

    private async ValueTask<Outcome<T>> ExecuteReservedAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        Reservation reservation)
    {
        try
        {
            while (true)
            {
                if (TryConsumeReservation(reservation, context.TimeProvider, out var wait, out var nextTurn))
                {
                    nextTurn?.TrySetResult(true);
                    break;
                }

                if (wait == Timeout.InfiniteTimeSpan)
                {
                    await reservation.WaitForTurnAsync(context.CancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await DelayHelper.CreateDelayTask(
                        context.TimeProvider,
                        wait,
                        context.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException cancelled)
        {
            CancelReservation(reservation)?.TrySetResult(true);
            return Outcome<T>.FromException(cancelled);
        }

        return await next.InvokeAsync(context).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAcquire(
        TimeProvider timeProvider,
        out Reservation? reservation,
        out TimeSpan? retryAfter)
    {
        if (_queueLimit > 0)
        {
            return TryAcquireWithQueue(timeProvider, out reservation, out retryAfter);
        }

        reservation = null;
        return TryAcquireWithoutQueue(timeProvider, out retryAfter);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAcquireWithoutQueue(TimeProvider timeProvider, out TimeSpan? retryAfter)
    {
        while (true)
        {
            var theoreticalArrival = Volatile.Read(ref _theoreticalArrival);
            var now = GetCurrentTimestamp(timeProvider);
            var delayTimestampUnits = theoreticalArrival - now - _burstTolerance;

            if (delayTimestampUnits > 0)
            {
                retryAfter = DelayHelper.FromSecondsClamped(delayTimestampUnits * SecondsPerSystemTimestamp);
                return false;
            }

            var arrival = Math.Max(now, theoreticalArrival);
            var nextArrival = arrival + _timestampUnitsPerPermit;
            if (nextArrival == arrival)
            {
                nextArrival = GetNextRepresentableTimestamp(arrival);
            }

            if (Interlocked.CompareExchange(ref _theoreticalArrival, nextArrival, theoreticalArrival) == theoreticalArrival)
            {
                retryAfter = null;
                return true;
            }
        }
    }

    private bool TryAcquireWithQueue(
        TimeProvider timeProvider,
        out Reservation? reservation,
        out TimeSpan? retryAfter)
    {
        lock (_queueGate)
        {
            var now = GetCurrentTimestamp(timeProvider);
            var theoreticalArrival = _theoreticalArrival;
            var delayTimestampUnits = theoreticalArrival - now - _burstTolerance;

            if (_queueHead is not null || delayTimestampUnits > 0)
            {
                if (_queuedReservations >= _queueLimit)
                {
                    reservation = null;
                    retryAfter = DelayHelper.FromSecondsClamped(
                        Math.Max(0, delayTimestampUnits) * SecondsPerSystemTimestamp);
                    return false;
                }

                var dueTimestamp = Math.Max(now, theoreticalArrival - _burstTolerance);
                reservation = new Reservation(dueTimestamp);
                EnqueueReservation(reservation);
            }
            else
            {
                reservation = null;
            }

            var arrival = Math.Max(now, theoreticalArrival);
            var nextArrival = arrival + _timestampUnitsPerPermit;
            _theoreticalArrival = nextArrival == arrival
                ? GetNextRepresentableTimestamp(arrival)
                : nextArrival;
            retryAfter = null;
            return true;
        }
    }

    private bool TryConsumeReservation(
        Reservation reservation,
        TimeProvider timeProvider,
        out TimeSpan wait,
        out TaskCompletionSource<bool>? nextTurn)
    {
        lock (_queueGate)
        {
            if (!ReferenceEquals(_queueHead, reservation))
            {
                wait = Timeout.InfiniteTimeSpan;
                nextTurn = null;
                return false;
            }

            var delayTimestampUnits = reservation.DueTimestamp - GetCurrentTimestamp(timeProvider);
            if (delayTimestampUnits > 0)
            {
                wait = DelayHelper.FromSecondsClamped(delayTimestampUnits * SecondsPerSystemTimestamp);
                nextTurn = null;
                return false;
            }

            nextTurn = RemoveReservation(reservation);
            wait = TimeSpan.Zero;
            return true;
        }
    }

    private TaskCompletionSource<bool>? CancelReservation(Reservation reservation)
    {
        lock (_queueGate)
        {
            if (!reservation.IsQueued)
            {
                return null;
            }

            for (var later = reservation.Next; later is not null; later = later.Next)
            {
                later.DueTimestamp -= _timestampUnitsPerPermit;
            }

            _theoreticalArrival -= _timestampUnitsPerPermit;
            return RemoveReservation(reservation);
        }
    }

    private void EnqueueReservation(Reservation reservation)
    {
        reservation.Previous = _queueTail;
        if (_queueTail is null)
        {
            _queueHead = reservation;
        }
        else
        {
            _queueTail.Next = reservation;
        }

        _queueTail = reservation;
        _queuedReservations++;
    }

    private TaskCompletionSource<bool>? RemoveReservation(Reservation reservation)
    {
        var wasHead = ReferenceEquals(_queueHead, reservation);
        if (reservation.Previous is null)
        {
            _queueHead = reservation.Next;
        }
        else
        {
            reservation.Previous.Next = reservation.Next;
        }

        if (reservation.Next is null)
        {
            _queueTail = reservation.Previous;
        }
        else
        {
            reservation.Next.Previous = reservation.Previous;
        }

        reservation.IsQueued = false;
        reservation.Previous = null;
        reservation.Next = null;
        _queuedReservations--;
        return wasHead ? _queueHead?.Turn : null;
    }

    private static double GetNextRepresentableTimestamp(double timestamp)
    {
        var bits = BitConverter.DoubleToInt64Bits(timestamp);
        return BitConverter.Int64BitsToDouble(timestamp < 0 ? bits - 1 : bits + 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetCurrentTimestamp(TimeProvider timeProvider) =>
        ReferenceEquals(timeProvider, TimeProvider.System)
            ? Stopwatch.GetTimestamp() - _systemTimestampOrigin
            : GetCustomTimestamp(timeProvider);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetCustomTimestamp(TimeProvider timeProvider)
    {
        var origin = _customTimestampOrigins.GetValue(
            timeProvider,
            static provider => new CustomTimestampOrigin(provider));
        var timestamp = timeProvider.GetTimestamp();
        var elapsedTimestamp = unchecked(timestamp - origin.ProviderTimestamp);

        return origin.SystemTimestamp - _systemTimestampOrigin
            + (elapsedTimestamp * origin.TimestampScale);
    }

    private sealed class CustomTimestampOrigin
    {
        public CustomTimestampOrigin(TimeProvider timeProvider)
        {
            SystemTimestamp = Stopwatch.GetTimestamp();
            ProviderTimestamp = timeProvider.GetTimestamp();
            TimestampScale = Stopwatch.Frequency / (double)timeProvider.TimestampFrequency;
        }

        public long SystemTimestamp { get; }

        public long ProviderTimestamp { get; }

        public double TimestampScale { get; }
    }

    private sealed class Reservation
    {
        public Reservation(double dueTimestamp) => DueTimestamp = dueTimestamp;

        public double DueTimestamp { get; set; }

        public Reservation? Previous { get; set; }

        public Reservation? Next { get; set; }

        public bool IsQueued { get; set; } = true;

        public TaskCompletionSource<bool> Turn { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitForTurnAsync(CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                await Turn.Task.ConfigureAwait(false);
                return;
            }

            var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                cancellation);

            await Task.WhenAny(Turn.Task, cancellation.Task).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
