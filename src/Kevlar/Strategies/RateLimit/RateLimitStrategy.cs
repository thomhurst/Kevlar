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
    protected internal override bool InvokesContinuationAtMostOnce => true;

    private static readonly double SecondsPerSystemTimestamp = 1d / Stopwatch.Frequency;

    private readonly long _systemTimestampOrigin = Stopwatch.GetTimestamp();
    private readonly ConditionalWeakTable<TimeProvider, CustomTimestampOrigin> _customTimestampOrigins = new();
    private readonly int _permits;
    private readonly TimeSpan _window;
    private readonly int _burst;
    private readonly int _queueLimit;
    private readonly double _timestampUnitsPerPermit;
    private readonly double _burstTolerance;
    private readonly Action<RateLimitRejectedEvent>? _onRejected;
    private readonly Func<RateLimitRejectedEvent, ValueTask>? _onRejectedAsync;
    private readonly string _telemetryName;
    private readonly Lock _metricsPublicationGate = new();
    private readonly HashSet<StrategyMetricAlias> _metricsAliases = [];
    private readonly object _queueGate = new();

    private double _theoreticalArrival = double.NegativeInfinity;
    private Reservation? _queueHead;
    private Reservation? _queueTail;
    private int _queuedReservations;
    private readonly KevlarMetrics.StateMetricRegistration<RateLimitStrategy> _metricsRegistration;

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    internal int Permits => _permits;

    internal TimeSpan Window => _window;

    internal int Burst => _burst;

    internal int QueueLimit => _queueLimit;

    internal bool HasNotification => _onRejected is not null || _onRejectedAsync is not null;

    public RateLimitStrategy(RateLimitOptions options)
    {
        ConfigurationValidation.ThrowIf(
            options.Permits <= 0,
            typeof(RateLimitOptions),
            nameof(options.Permits),
            options.Permits,
            "must be positive");
        ConfigurationValidation.ThrowIf(
            options.Window <= TimeSpan.Zero,
            typeof(RateLimitOptions),
            nameof(options.Window),
            options.Window,
            "must be positive");
        ConfigurationValidation.ThrowIf(
            options.Burst is <= 0,
            typeof(RateLimitOptions),
            nameof(options.Burst),
            options.Burst,
            "must be positive when set");
        ConfigurationValidation.ThrowIf(
            options.QueueLimit < 0,
            typeof(RateLimitOptions),
            nameof(options.QueueLimit),
            options.QueueLimit,
            "must not be negative");

        _permits = options.Permits;
        _window = options.Window;
        _burst = options.Burst ?? options.Permits;
        _queueLimit = options.QueueLimit;
        _timestampUnitsPerPermit = options.Window.TotalSeconds * Stopwatch.Frequency / options.Permits;
        _burstTolerance = (_burst - 1) * _timestampUnitsPerPermit;
        _onRejected = options.OnRejected;
        _onRejectedAsync = options.OnRejectedAsync;
        _telemetryName = options.Name ?? "RateLimit";
    }

    public override string Describe()
    {
        var queue = _queueLimit > 0 ? $", queue {_queueLimit}" : string.Empty;
        var burst = _burst != _permits ? $", burst {_burst}" : string.Empty;
        return $"RateLimit({_permits}/{DescribeHelper.Time(_window)}{burst}{queue})";
    }

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        RegisterMetricsAlias(
            new StrategyMetricAlias(context.ShieldName, context.StrategyIndex),
            context.TimeProvider);
        if (!TryAcquire(context.TimeProvider, out var reservation, out var retryAfter, out _))
        {
            return RejectAsync<T>(context, retryAfter);
        }

        return reservation is null
            ? next.InvokeAsync(context)
            : ExecuteReservedAsync(next, context, reservation);
    }

    private ValueTask<Outcome<T>> RejectAsync<T>(KevlarContext context, TimeSpan? retryAfter)
    {
        var rejection = new RateLimitExceededException(retryAfter);
        KevlarMetrics.Rejection(context, "rate_limit", rejection, _telemetryName);
        if (_onRejected is null && _onRejectedAsync is null)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection));
        }

        var rejectedEvent = new RateLimitRejectedEvent(
            retryAfter,
            _permits,
            _window,
            _burst,
            _queueLimit,
            context);

        CallbackInvoker.Invoke(
            _onRejected,
            rejectedEvent,
            CallbackErrorKind.RateLimitRejected,
            context);
        var notification = CallbackInvoker.InvokeAsync(
            _onRejectedAsync,
            rejectedEvent,
            CallbackErrorKind.RateLimitRejected,
            context);
        if (notification.IsCompletedSuccessfully)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection));
        }

        return AwaitRejectionAsync<T>(notification, rejection);
    }

    private static async ValueTask<Outcome<T>> AwaitRejectionAsync<T>(
        ValueTask notification,
        RateLimitExceededException rejection)
    {
        await notification.ConfigureAwait(false);
        return Outcome<T>.FromException(rejection);
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
        out TimeSpan? retryAfter,
        out double admissionTimestamp)
    {
        if (_queueLimit > 0)
        {
            admissionTimestamp = 0;
            return TryAcquireWithQueue(timeProvider, out reservation, out retryAfter);
        }

        reservation = null;
        return TryAcquireWithoutQueue(timeProvider, out retryAfter, out admissionTimestamp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAcquireWithoutQueue(
        TimeProvider timeProvider,
        out TimeSpan? retryAfter,
        out double admissionTimestamp)
    {
        while (true)
        {
            var theoreticalArrival = Volatile.Read(ref _theoreticalArrival);
            var now = GetCurrentTimestamp(timeProvider);
            var delayTimestampUnits = theoreticalArrival - now - _burstTolerance;

            if (delayTimestampUnits > 0)
            {
                retryAfter = GetRetryAfter(delayTimestampUnits);
                admissionTimestamp = 0;
                return false;
            }

            var nextArrival = GetNextArrival(theoreticalArrival, now);

            if (Interlocked.CompareExchange(ref _theoreticalArrival, nextArrival, theoreticalArrival) == theoreticalArrival)
            {
                retryAfter = null;
                admissionTimestamp = now;
                return true;
            }
        }
    }

    private double GetNextArrival(double theoreticalArrival, double now)
    {
        var arrival = Math.Max(now, theoreticalArrival);
        var nextArrival = arrival + _timestampUnitsPerPermit;
        return nextArrival == arrival
            ? GetNextRepresentableTimestamp(arrival)
            : nextArrival;
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
                    retryAfter = GetRetryAfter(Math.Max(0, delayTimestampUnits));
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

    private static TimeSpan GetRetryAfter(double delayTimestampUnits)
    {
        var seconds = delayTimestampUnits * SecondsPerSystemTimestamp;
        if (double.IsNaN(seconds) || seconds <= 0)
        {
            return TimeSpan.Zero;
        }

        // Scaling through provider timestamp units can lose a few ULPs at TimeSpan.MaxValue.
        const double doubleMachineEpsilon = 2.2204460492503131e-16;
        var maximumSeconds = TimeSpan.MaxValue.TotalSeconds;
        var maximumRoundingTolerance = maximumSeconds * (4 * doubleMachineEpsilon);
        return seconds >= maximumSeconds - maximumRoundingTolerance
            ? TimeSpan.MaxValue
            : TimeSpan.FromSeconds(seconds);
    }

    private void RegisterMetricsAlias(StrategyMetricAlias alias, TimeProvider timeProvider)
    {
        if (KevlarMetrics.RateStateEnabled)
        {
            _metricsRegistration.Add(alias, timeProvider);
        }
    }

    internal (long Available, int Queued) CaptureState(TimeProvider timeProvider)
    {
        double theoreticalArrival;
        int queued;
        if (_queueLimit > 0)
        {
            lock (_queueGate)
            {
                theoreticalArrival = _theoreticalArrival;
                queued = _queuedReservations;
            }
        }
        else
        {
            theoreticalArrival = Volatile.Read(ref _theoreticalArrival);
            queued = 0;
        }

        long available;
        if (queued > 0)
        {
            available = 0;
        }
        else if (double.IsNegativeInfinity(theoreticalArrival))
        {
            available = _burst;
        }
        else if (double.IsInfinity(_timestampUnitsPerPermit))
        {
            available = 0;
        }
        else
        {
            var debt = Math.Max(0, theoreticalArrival - GetCurrentTimestamp(timeProvider));
            var consumed = Math.Ceiling(debt / _timestampUnitsPerPermit);
            available = consumed >= _burst ? 0 : _burst - (long)consumed;
        }

        return (available, queued);
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
