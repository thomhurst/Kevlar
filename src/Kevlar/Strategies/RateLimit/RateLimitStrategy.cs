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

    private readonly int _permits;
    private readonly TimeSpan _window;
    private readonly int _burst;
    private readonly int _queueLimit;
    private readonly double _timestampUnitsPerPermit;
    private readonly double _burstTolerance;
    private readonly double _queueTolerance;

    private double _theoreticalArrival = double.NegativeInfinity;

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
        _queueTolerance = _queueLimit * _timestampUnitsPerPermit;
    }

    public override string Describe()
    {
        var queue = _queueLimit > 0 ? $", queue {_queueLimit}" : string.Empty;
        var burst = _burst != _permits ? $", burst {_burst}" : string.Empty;
        return $"RateLimit({_permits}/{DescribeHelper.Time(_window)}{burst}{queue})";
    }

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (!TryAcquire(context.TimeProvider, out var wait, out var retryAfter))
        {
            KevlarMetrics.Rejection(context.ShieldName, "rate_limit");
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(new RateLimitExceededException(retryAfter)));
        }

        return wait > TimeSpan.Zero
            ? ExecuteAfterDelayAsync(next, context, wait)
            : next.InvokeAsync(context);
    }

    private static async ValueTask<Outcome<T>> ExecuteAfterDelayAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        TimeSpan wait)
    {
        try
        {
            await DelayHelper.DelayAsync(context, wait).ConfigureAwait(false);
        }
        catch (OperationCanceledException cancelled)
        {
            return Outcome<T>.FromException(cancelled);
        }

        return await next.InvokeAsync(context).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAcquire(TimeProvider timeProvider, out TimeSpan wait, out TimeSpan? retryAfter)
    {
        var now = ReferenceEquals(timeProvider, TimeProvider.System)
            ? Stopwatch.GetTimestamp()
            : timeProvider.GetTimestamp() * (Stopwatch.Frequency / (double)timeProvider.TimestampFrequency);

        while (true)
        {
            var theoreticalArrival = Volatile.Read(ref _theoreticalArrival);
            var delayTimestampUnits = theoreticalArrival - _burstTolerance - now;

            if (delayTimestampUnits > _queueTolerance)
            {
                wait = TimeSpan.Zero;
                retryAfter = TimeSpan.FromSeconds(delayTimestampUnits * SecondsPerSystemTimestamp);
                return false;
            }

            var nextArrival = Math.Max(now, theoreticalArrival) + _timestampUnitsPerPermit;
            if (Interlocked.CompareExchange(ref _theoreticalArrival, nextArrival, theoreticalArrival) == theoreticalArrival)
            {
                wait = delayTimestampUnits > 0
                    ? TimeSpan.FromSeconds(delayTimestampUnits * SecondsPerSystemTimestamp)
                    : TimeSpan.Zero;
                retryAfter = null;
                return true;
            }
        }
    }
}
