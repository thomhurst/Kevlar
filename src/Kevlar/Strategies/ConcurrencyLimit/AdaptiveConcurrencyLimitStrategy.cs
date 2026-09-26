using System.Runtime.CompilerServices;
using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class AdaptiveConcurrencyLimitStrategy : Strategy, IConcurrencyLimitState
{
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<TimeProvider, TimestampOrigin> _timestampOrigins = new();
    private readonly Func<ConcurrencyLimitRejectedEvent, ValueTask>? _onRejected;
    private readonly string _telemetryName;
    private readonly KevlarMetrics.StateMetricRegistration<IConcurrencyLimitState> _metricsRegistration;
    // Limit and running count share one CAS word, so admission cannot race a limit reduction.
    private long _state;
    private double _latestTimestamp;
    private double _windowStartedAt;
    private double _successfulDurationTicks;
    private double _baselineTicks;
    private long _successes;
    private int _peakRunning;
    private bool _hasFailure;

    public AdaptiveConcurrencyLimitStrategy(AdaptiveConcurrencyLimitOptions options)
    {
        var type = typeof(AdaptiveConcurrencyLimitOptions);
        ConfigurationValidation.ThrowIf(options.Algorithm != AdaptiveConcurrencyLimitAlgorithm.Aimd,
            type, nameof(options.Algorithm), options.Algorithm, "must be Aimd");
        ConfigurationValidation.ThrowIf(options.MinLimit <= 0,
            type, nameof(options.MinLimit), options.MinLimit, "must be positive");
        ConfigurationValidation.ThrowIf(options.MaxLimit < options.MinLimit,
            type, nameof(options.MaxLimit), options.MaxLimit, "must be at least MinLimit");
        ConfigurationValidation.ThrowIf(options.InitialLimit < options.MinLimit || options.InitialLimit > options.MaxLimit,
            type, nameof(options.InitialLimit), options.InitialLimit, "must be between MinLimit and MaxLimit");
        ConfigurationValidation.ThrowIf(options.SamplingWindow <= TimeSpan.Zero,
            type, nameof(options.SamplingWindow), options.SamplingWindow, "must be positive");
        ConfigurationValidation.ThrowIf(double.IsNaN(options.DecreaseFactor) || options.DecreaseFactor <= 0 || options.DecreaseFactor >= 1,
            type, nameof(options.DecreaseFactor), options.DecreaseFactor, "must be greater than zero and less than one");
        ConfigurationValidation.ThrowIf(double.IsNaN(options.LatencyTolerance) || double.IsInfinity(options.LatencyTolerance) || options.LatencyTolerance < 1,
            type, nameof(options.LatencyTolerance), options.LatencyTolerance, "must be finite and at least one");

        MinLimit = options.MinLimit;
        MaxLimit = options.MaxLimit;
        InitialLimit = options.InitialLimit;
        SamplingWindow = options.SamplingWindow;
        DecreaseFactor = options.DecreaseFactor;
        LatencyTolerance = options.LatencyTolerance;
        _onRejected = options.OnRejected;
        _telemetryName = options.Name ?? "AdaptiveConcurrencyLimit";
        _state = (long)InitialLimit << 32;
        _metricsRegistration = KevlarMetrics.RegisterConcurrencyStateSource(this);
    }

    protected internal override bool InvokesContinuationAtMostOnce => true;

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    internal int MinLimit { get; }

    internal int MaxLimit { get; }

    internal int InitialLimit { get; }

    internal TimeSpan SamplingWindow { get; }

    internal double DecreaseFactor { get; }

    internal double LatencyTolerance { get; }

    internal bool HasNotification => _onRejected is not null;

    internal int CurrentLimit => (int)(Volatile.Read(ref _state) >> 32);

    int IConcurrencyLimitState.MaxConcurrency => MaxLimit;

    int IConcurrencyLimitState.CurrentLimit => CurrentLimit;

    public (int Available, int Running, int Queued) CaptureState()
    {
        var state = CaptureAdaptiveState();
        return (state.Available, state.Running, 0);
    }

    internal (int Limit, int Available, int Running) CaptureAdaptiveState()
    {
        var state = Volatile.Read(ref _state);
        var limit = (int)(state >> 32);
        var running = (int)(state & uint.MaxValue);
        return (limit, Math.Max(0, limit - running), running);
    }

    public override string Describe() =>
        $"AdaptiveConcurrencyLimit(AIMD, initial {InitialLimit}, min {MinLimit}, max {MaxLimit}, window {DescribeHelper.Time(SamplingWindow)})";

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (KevlarMetrics.ConcurrencyStateEnabled)
        {
            _metricsRegistration.Add(new StrategyMetricAlias(context.ShieldName, context.StrategyIndex));
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        var startedAt = context.TimeProvider.GetTimestamp();
        if (!TryAcquire(out var running, out var limit))
        {
            return RejectAsync<T>(context, limit);
        }

        ValueTask<Outcome<T>> execution;
        try
        {
            execution = next.InvokeAsync(context);
        }
        catch (Exception exception)
        {
            return new ValueTask<Outcome<T>>(Complete(Outcome<T>.FromException(exception), context, startedAt, running));
        }

        return execution.IsCompletedSuccessfully
            ? new ValueTask<Outcome<T>>(Complete(execution.Result, context, startedAt, running))
            : AwaitExecutionAsync(execution, context, startedAt, running);
    }

    private bool TryAcquire(out int running, out int limit)
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            limit = (int)(state >> 32);
            running = (int)(state & uint.MaxValue);
            if (running >= limit)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _state, state + 1, state) == state)
            {
                running++;
                return true;
            }
        }
    }

#if NET8_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    private async ValueTask<Outcome<T>> AwaitExecutionAsync<T>(
        ValueTask<Outcome<T>> execution, KevlarContext context, long startedAt, int running)
    {
        Outcome<T> outcome;
        try
        {
            outcome = await execution.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            outcome = Outcome<T>.FromException(exception);
        }

        return Complete(outcome, context, startedAt, running);
    }

    private Outcome<T> Complete<T>(Outcome<T> outcome, KevlarContext context, long startedAt, int running)
    {
        try
        {
            if (outcome.Exception is not OperationCanceledException || !context.CancellationToken.IsCancellationRequested)
            {
                var completedAt = context.TimeProvider.GetTimestamp();
                var duration = context.TimeProvider.GetElapsedTime(startedAt, completedAt);
                Observe(context.TimeProvider, completedAt, Math.Max(0, duration.Ticks), outcome.Exception is not null, running);
            }

            return outcome;
        }
        finally
        {
            Interlocked.Decrement(ref _state);
        }
    }

    private void Observe(TimeProvider timeProvider, long completedAt, long durationTicks, bool failed, int running)
    {
        lock (_gate)
        {
            var timestamp = GetTimestamp(timeProvider, completedAt);
            _peakRunning = Math.Max(_peakRunning, running);
            _hasFailure |= failed;
            if (!failed)
            {
                _successes++;
                _successfulDurationTicks += durationTicks;
            }

            if (timestamp - _windowStartedAt < SamplingWindow.Ticks)
            {
                return;
            }

            var meanTicks = _successes == 0 ? 0 : _successfulDurationTicks / _successes;
            if (_baselineTicks == 0 && _successes > 0)
            {
                _baselineTicks = Math.Max(1, meanTicks);
            }

            var limit = CurrentLimit;
            var congested = _hasFailure || (_successes > 0 && meanTicks > _baselineTicks * LatencyTolerance);
            var nextLimit = limit;
            if (congested)
            {
                nextLimit = Math.Max(MinLimit, (int)(limit * DecreaseFactor));
            }
            else if (_successes > 0 && (long)_peakRunning * 2 >= limit && limit < MaxLimit)
            {
                nextLimit++;
            }

            SetLimit(nextLimit);
            if (_successes > 0)
            {
                _baselineTicks = Math.Max(1, _baselineTicks + 0.1 * (meanTicks - _baselineTicks));
            }

            _windowStartedAt = timestamp;
            _successfulDurationTicks = 0;
            _successes = 0;
            _peakRunning = 0;
            _hasFailure = false;
        }
    }

    private double GetTimestamp(TimeProvider timeProvider, long timestamp)
    {
        if (!_timestampOrigins.TryGetValue(timeProvider, out var origin))
        {
            origin = new TimestampOrigin(timestamp, _latestTimestamp, TimeSpan.TicksPerSecond / (double)timeProvider.TimestampFrequency);
            _timestampOrigins.Add(timeProvider, origin);
        }

        _latestTimestamp = Math.Max(_latestTimestamp,
            origin.Timeline + unchecked(timestamp - origin.Timestamp) * origin.Scale);
        return _latestTimestamp;
    }

    private void SetLimit(int limit)
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            var updated = ((long)limit << 32) | (state & uint.MaxValue);
            if (Interlocked.CompareExchange(ref _state, updated, state) == state)
            {
                return;
            }
        }
    }

    private ValueTask<Outcome<T>> RejectAsync<T>(KevlarContext context, int limit)
    {
        var rejection = new ConcurrencyLimitExceededException();
        KevlarMetrics.Rejection(context, "concurrency_limit", rejection, _telemetryName);
        if (_onRejected is null)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection));
        }

        var notification = CallbackInvoker.InvokeAsync(_onRejected,
            new ConcurrencyLimitRejectedEvent(limit, queueLimit: 0, context),
            CallbackErrorKind.ConcurrencyLimitRejected, context, "AdaptiveConcurrencyLimitOptions.OnRejected");
        return notification.IsCompletedSuccessfully
            ? new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection))
            : AwaitRejectionAsync<T>(notification, rejection);
    }

    private static async ValueTask<Outcome<T>> AwaitRejectionAsync<T>(ValueTask notification, Exception rejection)
    {
        await notification.ConfigureAwait(false);
        return Outcome<T>.FromException(rejection);
    }

    private sealed class TimestampOrigin(long timestamp, double timeline, double scale)
    {
        public long Timestamp { get; } = timestamp;

        public double Timeline { get; } = timeline;

        public double Scale { get; } = scale;
    }
}
