using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Kevlar.Internal;

namespace Kevlar.Strategies;

/// <summary>
/// The thread-safe state machine behind a circuit breaker. Shared by the strategy
/// (which drives it from executions) and the monitor (manual control).
/// </summary>
internal sealed class CircuitBreakerCore
{
    private const int BucketCount = 10;
    private static readonly double SecondsPerSystemTimestamp = 1d / Stopwatch.Frequency;

    private readonly Lock _gate = new();
    private readonly Lock _telemetryGate = new();
    private readonly ConditionalWeakTable<TimeProvider, TimestampOrigin> _timestampOrigins = new();
    private readonly int? _consecutiveFailureLimit;
    private readonly double? _failureRatio;
    private readonly int _minimumThroughput;
    private readonly TimeSpan _samplingWindow;
    private readonly double _bucketDurationTimestampUnits;
    private readonly TimeSpan _breakDuration;
    private readonly double _breakDurationTimestampUnits;
    private readonly Action<CircuitState> _recordState;
    private readonly CircuitBreakerBreakDurationGenerator? _breakDurationGenerator;
    private readonly Func<CircuitBreakerStateChangedEvent, ValueTask>? _onStateChanged;
    private readonly CircuitBreakerMonitor? _monitor;
    private readonly Type _optionsType;
    private readonly string _telemetryName;
    private readonly Queue<TransitionPublication> _pendingTransitions = new();
    private readonly AsyncLocal<TransitionPublication?>? _ambientPublication;

    private readonly RatioBucket?[] _ratioBuckets = new RatioBucket?[BucketCount];
    private TimestampOrigin? _systemTimestampOrigin;
    private RatioBucket? _currentRatioBucket;
    private double _currentBucketStart = double.NaN;
    private double _currentBucketEnd = double.NaN;
    private int _currentBucketIndex;
    private int _systemRatioFastPathEnabled = 1;

    private volatile CircuitState _state = CircuitState.Closed;
    private double _latestTimestamp;
    private double _openUntilTimestamp;
    private int _consecutiveFailures;
    private bool _probeInFlight;
    private long _admissionGeneration;
    private Exception? _lastException;
    private TimeProvider? _openTimeProvider;
    private long _openingGeneration;
    private bool _openingPending;
    private bool _isPublishing;
    private int _publishingThreadId;
    private TransitionPublication? _activePublication;
    private CircuitTelemetryRegistration[] _telemetryRegistrations = [];

    internal void AttachTelemetryListener(
        IKevlarTelemetryListener? previous,
        IKevlarTelemetryListener listener,
        string? shieldName,
        int strategyIndex,
        object? scopeOwner = null)
    {
        lock (_telemetryGate)
        {
            var registrations = _telemetryRegistrations;
            var updated = new List<CircuitTelemetryRegistration>(registrations.Length + 1);
            var registeredForShield = false;
            foreach (var registration in registrations)
            {
                if (!registration.Listener.TryGetTarget(out var registered))
                {
                    continue;
                }

                if (registration.ScopeOwner is { } owner
                    && !owner.TryGetTarget(out _))
                {
                    continue;
                }

                if (previous is not null
                    && ReferenceEquals(registered, previous)
                    && registration.ShieldName == shieldName
                    && registration.StrategyIndex == strategyIndex
                    && registration.HasScopeOwner(scopeOwner))
                {
                    continue;
                }

                if (ReferenceEquals(registered, listener)
                    && registration.ShieldName == shieldName
                    && registration.StrategyIndex == strategyIndex
                    && registration.HasScopeOwner(scopeOwner))
                {
                    registeredForShield = true;
                }

                updated.Add(registration);
            }

            if (!registeredForShield)
            {
                updated.Add(new CircuitTelemetryRegistration(
                    listener,
                    shieldName,
                    strategyIndex,
                    scopeOwner));
            }

            Volatile.Write(ref _telemetryRegistrations, updated.ToArray());
        }
    }

    public CircuitBreakerCore(
        CircuitBreakerOptions options,
        CircuitBreakerBreakDurationGenerator? breakDurationGenerator,
        Action<CircuitState> recordState,
        Type optionsType)
    {
        ConfigurationValidation.ThrowIf(
            options.ConsecutiveFailures is <= 0,
            optionsType,
            nameof(options.ConsecutiveFailures),
            options.ConsecutiveFailures,
            "must be positive when set");
        ConfigurationValidation.ThrowIf(
            options.FailureRatio is { } ratio && (double.IsNaN(ratio) || ratio <= 0 || ratio > 1),
            optionsType,
            nameof(options.FailureRatio),
            options.FailureRatio,
            "must be between 0 (exclusive) and 1 (inclusive)");
        ConfigurationValidation.ThrowIf(
            options.ConsecutiveFailures is not null && options.FailureRatio is not null,
            optionsType,
            $"{nameof(options.ConsecutiveFailures)} and {nameof(options.FailureRatio)}",
            $"{options.ConsecutiveFailures} and {options.FailureRatio}",
            "select different trip modes and cannot both be set; " +
            "Clear ConsecutiveFailures to trip on the failure ratio within SamplingWindow or clear " +
            "FailureRatio to trip on consecutive failures, or leave both unset to trip after 5 " +
            "consecutive failures");
        ConfigurationValidation.ThrowIf(
            options.MinimumThroughput < 1,
            optionsType,
            nameof(options.MinimumThroughput),
            options.MinimumThroughput,
            "must be at least 1");
        ConfigurationValidation.ThrowIf(
            options.SamplingWindow <= TimeSpan.Zero,
            optionsType,
            nameof(options.SamplingWindow),
            options.SamplingWindow,
            "must be positive");
        ConfigurationValidation.ThrowIf(
            options.BreakDuration <= TimeSpan.Zero,
            optionsType,
            nameof(options.BreakDuration),
            options.BreakDuration,
            "must be positive");

        _failureRatio = options.FailureRatio;
        _consecutiveFailureLimit = options.FailureRatio is null ? options.ConsecutiveFailures ?? 5 : null;
        _samplingWindow = options.SamplingWindow;
        _minimumThroughput = options.MinimumThroughput;
        _bucketDurationTimestampUnits = options.SamplingWindow.TotalSeconds * Stopwatch.Frequency / BucketCount;
        _breakDuration = options.BreakDuration;
        _breakDurationTimestampUnits = options.BreakDuration.TotalSeconds * Stopwatch.Frequency;
        _recordState = recordState;
        _breakDurationGenerator = breakDurationGenerator;
        _onStateChanged = options.OnStateChanged;
        _optionsType = optionsType;
        _telemetryName = options.Name ?? "CircuitBreaker";
        _ambientPublication = options.OnStateChanged is null
            ? null
            : new AsyncLocal<TransitionPublication?>();
        _monitor = options.Monitor;
    }

    internal void BindMonitor() => _monitor?.Bind(this);

    public string Describe() =>
        _consecutiveFailureLimit is { } limit
            ? $"CircuitBreaker({limit} consecutive, break {DescribeBreakDuration()})"
            : FormattableString.Invariant(
                $"CircuitBreaker({_failureRatio!.Value * 100:0.#}% over {DescribeHelper.Time(_samplingWindow)}, min {_minimumThroughput}, break {DescribeBreakDuration()})");

    /// <summary>
    /// Whether the strategy must take the awaitable entry/record paths so configured hooks are
    /// awaited. Hooks that complete synchronously keep those paths synchronous.
    /// </summary>
    public bool RequiresAsyncExecution => _breakDurationGenerator is not null || _onStateChanged is not null;

    internal string TelemetryName => _telemetryName;

    private string DescribeBreakDuration() => _breakDurationGenerator is null
        ? DescribeHelper.Time(_breakDuration)
        : "dynamic";

    internal int? ConsecutiveFailures => _consecutiveFailureLimit;

    internal double? FailureRatio => _failureRatio;

    internal int MinimumThroughput => _minimumThroughput;

    internal TimeSpan SamplingWindow => _samplingWindow;

    internal TimeSpan BreakDuration => _breakDuration;

    internal bool HasMonitor => _monitor is not null;

    internal bool HasNotification => _onStateChanged is not null;

    public CircuitState State
    {
        get
        {
            lock (_gate)
            {
                return GetReportedState(_openTimeProvider);
            }
        }
    }

    internal CircuitState GetState(TimeProvider timeProvider)
    {
        lock (_gate)
        {
            return GetReportedState(timeProvider);
        }
    }

    /// <summary>
    /// Gates an execution. Returns <see langword="false"/> with a rejection when the circuit
    /// refuses it; a <see langword="true"/> return during half-open marks a probe in flight,
    /// so the caller must report back via Record* or <see cref="AbandonProbe(long)"/>.
    /// </summary>
    public bool TryEnter(
        TimeProvider timeProvider,
        KevlarContext context,
        out CircuitOpenException? rejection,
        out long admissionGeneration)
    {
        var allowed = TryEnterCore(
            timeProvider,
            context,
            out rejection,
            out var transition,
            out admissionGeneration);

        try
        {
            Publish(transition);
        }
        catch
        {
            if (transition?.StateChange.To == CircuitState.HalfOpen)
            {
                AbandonProbe(admissionGeneration);
            }

            throw;
        }

        return allowed;
    }

    public ValueTask<EntryResult> TryEnterAsync(TimeProvider timeProvider, KevlarContext context)
    {
        var allowed = TryEnterCore(
            timeProvider,
            context,
            out var rejection,
            out var transition,
            out var admissionGeneration);
        ValueTask publication;
        try
        {
            publication = PublishAsync(transition);
        }
        catch
        {
            if (transition?.StateChange.To == CircuitState.HalfOpen)
            {
                AbandonProbe(admissionGeneration);
            }

            throw;
        }

        if (publication.IsCompletedSuccessfully)
        {
            publication.GetAwaiter().GetResult();
            return new ValueTask<EntryResult>(new EntryResult(allowed, rejection, admissionGeneration));
        }

        return AwaitEntryPublicationAsync(
            publication,
            allowed,
            rejection,
            transition,
            admissionGeneration);
    }

    private bool TryEnterCore(
        TimeProvider timeProvider,
        KevlarContext context,
        out CircuitOpenException? rejection,
        out TransitionPublication? transition,
        out long admissionGeneration)
    {
        transition = null;
        admissionGeneration = 0;
        rejection = null;

        var observedGeneration = Volatile.Read(ref _admissionGeneration);
        if (_state == CircuitState.Closed
            && observedGeneration == Volatile.Read(ref _admissionGeneration))
        {
            // Closed executions reserve no exclusive state. A concurrent transition increments
            // the generation so its in-flight outcome cannot affect the new circuit generation.
            admissionGeneration = observedGeneration;
            return true;
        }

        lock (_gate)
        {
            switch (_state)
            {
                case CircuitState.Closed:
                    admissionGeneration = _admissionGeneration;
                    return true;

                case CircuitState.Isolated:
                    rejection = new CircuitOpenException(null, isIsolated: true, _lastException);
                    return false;

                case CircuitState.Open:
                    var timestamp = GetCurrentTimestamp(timeProvider);
                    if (timestamp < _openUntilTimestamp)
                    {
                        rejection = new CircuitOpenException(
                            GetElapsedTime(_openUntilTimestamp - timestamp),
                            isIsolated: false,
                            _lastException);
                        return false;
                    }

                    transition = ChangeState(CircuitState.HalfOpen, context);
                    _probeInFlight = true;
                    admissionGeneration = _admissionGeneration;
                    return true;

                default: // HalfOpen
                    if (_probeInFlight)
                    {
                        rejection = new CircuitOpenException(null, isIsolated: false, _lastException);
                        return false;
                    }

                    _probeInFlight = true;
                    admissionGeneration = _admissionGeneration;
                    return true;
            }
        }
    }

    private async ValueTask<EntryResult> AwaitEntryPublicationAsync(
        ValueTask publication,
        bool allowed,
        CircuitOpenException? rejection,
        TransitionPublication? transition,
        long admissionGeneration)
    {
        try
        {
            await publication.ConfigureAwait(false);
            return new EntryResult(allowed, rejection, admissionGeneration);
        }
        catch
        {
            if (transition?.StateChange.To == CircuitState.HalfOpen)
            {
                AbandonProbe(admissionGeneration);
            }

            throw;
        }
    }

    public readonly record struct EntryResult(
        bool Allowed,
        CircuitOpenException? Rejection,
        long AdmissionGeneration);

    public void RecordSuccess(
        TimeProvider timeProvider,
        KevlarContext context,
        long admissionGeneration)
    {
        if (TryRecordRatioSuccessFast(timeProvider, admissionGeneration))
        {
            return;
        }

        Publish(RecordSuccessCore(timeProvider, context, admissionGeneration));
    }

    public ValueTask RecordSuccessAsync(
        TimeProvider timeProvider,
        KevlarContext context,
        long admissionGeneration)
    {
        return TryRecordRatioSuccessFast(timeProvider, admissionGeneration)
            ? default
            : PublishAsync(RecordSuccessCore(timeProvider, context, admissionGeneration));
    }

    private bool TryRecordRatioSuccessFast(
        TimeProvider timeProvider,
        long admissionGeneration)
    {
        if (_failureRatio is null
            || Volatile.Read(ref _systemRatioFastPathEnabled) == 0
            || !ReferenceEquals(timeProvider, TimeProvider.System))
        {
            return false;
        }

        var origin = Volatile.Read(ref _systemTimestampOrigin);
        var bucket = Volatile.Read(ref _currentRatioBucket);
        if (origin is null || bucket is null)
        {
            return false;
        }

        var providerTimestamp = Stopwatch.GetTimestamp();
        var elapsedTimestamp = unchecked(providerTimestamp - origin.ProviderTimestamp);
        var timestamp = origin.TimelineTimestamp + (elapsedTimestamp * origin.TimestampScale);
        if (timestamp >= Volatile.Read(ref _currentBucketEnd)
            || Volatile.Read(ref _systemRatioFastPathEnabled) == 0
            || _state != CircuitState.Closed
            || admissionGeneration != Volatile.Read(ref _admissionGeneration))
        {
            return false;
        }

        // Bucket instances are never reused. A concurrent rollover or reset can detach this
        // bucket, making a late increment harmless instead of racing an in-place clear.
        Volatile.Write(ref _consecutiveFailures, 0);
        Interlocked.Increment(ref bucket.Successes);
        return true;
    }

    private TransitionPublication? RecordSuccessCore(
        TimeProvider timeProvider,
        KevlarContext context,
        long admissionGeneration)
    {
        lock (_gate)
        {
            if (admissionGeneration != _admissionGeneration)
            {
                return null;
            }

            if (_state == CircuitState.HalfOpen)
            {
                _probeInFlight = false;
                CancelPendingOpening();
                ResetMetrics();
                return ChangeState(CircuitState.Closed, context);
            }

            if (_state == CircuitState.Closed)
            {
                Volatile.Write(ref _consecutiveFailures, 0);
                if (_failureRatio is not null)
                {
                    var bucket = AdvanceBucket(timeProvider);
                    Interlocked.Increment(ref bucket.Successes);
                }
            }

            return null;
        }
    }

    public void RecordFailure(
        TimeProvider timeProvider,
        Exception? exception,
        KevlarContext context,
        long admissionGeneration)
    {
        Publish(RecordFailureCore(timeProvider, exception, context, admissionGeneration));
    }

    public ValueTask RecordFailureAsync<T>(
        TimeProvider timeProvider,
        in Outcome<T> outcome,
        KevlarContext context,
        long admissionGeneration)
    {
        if (_breakDurationGenerator is null)
        {
            return PublishAsync(RecordFailureCore(
                timeProvider,
                outcome.Exception,
                context,
                admissionGeneration));
        }

        if (!TryReserveDynamicOpening(
                timeProvider,
                outcome.Exception,
                context,
                admissionGeneration,
                out var reservation))
        {
            return default;
        }

        ValueTask<TimeSpan> generation;
        try
        {
            var statistics = reservation.Statistics;
            generation = _breakDurationGenerator.Invoke(
                in outcome,
                in statistics,
                context);
            SynchronousExecutionGuard.ThrowIfIncomplete(
                in generation,
                context,
                "CircuitBreakerOptions.BreakDurationGenerator");
        }
        catch
        {
            CancelPendingOpening(reservation);
            throw;
        }

        if (!generation.IsCompletedSuccessfully)
        {
            return AwaitBreakDurationAsync(generation, reservation, timeProvider);
        }

        TimeSpan duration;
        try
        {
            duration = generation.Result;
            ValidateGeneratedBreakDuration(duration);
        }
        catch
        {
            CancelPendingOpening(reservation);
            throw;
        }

        return PublishAsync(CommitDynamicOpening(reservation, timeProvider, duration));
    }

    private TransitionPublication? RecordFailureCore(
        TimeProvider timeProvider,
        Exception? exception,
        KevlarContext context,
        long admissionGeneration)
    {
        lock (_gate)
        {
            if (admissionGeneration != _admissionGeneration)
            {
                return null;
            }

            if (_state == CircuitState.HalfOpen)
            {
                _probeInFlight = false;
                _lastException = exception;
                _openTimeProvider = timeProvider;
                _openUntilTimestamp = GetCurrentTimestamp(timeProvider) + _breakDurationTimestampUnits;
                return ChangeState(CircuitState.Open, context);
            }

            if (_state == CircuitState.Closed)
            {
                var timestamp = _failureRatio is null
                    ? 0
                    : GetCurrentTimestamp(timeProvider);

                if (RecordFailureAndCheckThreshold(timestamp, out _))
                {
                    if (_failureRatio is null)
                    {
                        timestamp = GetCurrentTimestamp(timeProvider);
                    }

                    _lastException = exception;
                    _openTimeProvider = timeProvider;
                    _openUntilTimestamp = timestamp + _breakDurationTimestampUnits;
                    return ChangeState(CircuitState.Open, context);
                }
            }

            return null;
        }
    }

    private bool TryReserveDynamicOpening(
        TimeProvider timeProvider,
        Exception? exception,
        KevlarContext context,
        long admissionGeneration,
        out OpeningReservation reservation)
    {
        lock (_gate)
        {
            reservation = default;
            if (admissionGeneration != _admissionGeneration)
            {
                return false;
            }

            if (_openingPending)
            {
                if (_state == CircuitState.Closed)
                {
                    _ = RecordFailureAndCheckThreshold(
                        _failureRatio is null ? 0 : GetCurrentTimestamp(timeProvider),
                        out _);
                }

                return false;
            }

            CircuitBreakerFailureStatistics statistics;
            bool shouldOpen;
            if (_state == CircuitState.HalfOpen)
            {
                statistics = new CircuitBreakerFailureStatistics(
                    FailureRate: 1,
                    FailureCount: 1,
                    ConsecutiveFailures: 1);
                shouldOpen = true;
            }
            else if (_state == CircuitState.Closed)
            {
                shouldOpen = RecordFailureAndCheckThreshold(
                    _failureRatio is null ? 0 : GetCurrentTimestamp(timeProvider),
                    out statistics);
            }
            else
            {
                statistics = default;
                shouldOpen = false;
            }

            if (!shouldOpen)
            {
                return false;
            }

            _openingPending = true;
            reservation = new OpeningReservation(
                ++_openingGeneration,
                admissionGeneration,
                _state,
                exception,
                context,
                statistics);
            return true;
        }
    }

    private async ValueTask AwaitBreakDurationAsync(
        ValueTask<TimeSpan> generation,
        OpeningReservation reservation,
        TimeProvider timeProvider)
    {
        TimeSpan duration;
        try
        {
            duration = await generation.ConfigureAwait(false);
            ValidateGeneratedBreakDuration(duration);
        }
        catch
        {
            CancelPendingOpening(reservation);
            throw;
        }

        await PublishAsync(CommitDynamicOpening(reservation, timeProvider, duration)).ConfigureAwait(false);
    }

    private TransitionPublication? CommitDynamicOpening(
        OpeningReservation reservation,
        TimeProvider timeProvider,
        TimeSpan duration)
    {
        lock (_gate)
        {
            if (!_openingPending
                || _openingGeneration != reservation.Generation
                || _admissionGeneration != reservation.AdmissionGeneration
                || _state != reservation.ExpectedState)
            {
                return null;
            }

            _openingPending = false;
            _probeInFlight = false;
            _lastException = reservation.Exception;
            _openTimeProvider = timeProvider;
            _openUntilTimestamp = GetCurrentTimestamp(timeProvider)
                + (duration.TotalSeconds * Stopwatch.Frequency);
            return ChangeState(CircuitState.Open, reservation.Context, duration);
        }
    }

    private void CancelPendingOpening(OpeningReservation reservation)
    {
        lock (_gate)
        {
            if (_openingPending && _openingGeneration == reservation.Generation)
            {
                CancelPendingOpening();
                if (_state == CircuitState.HalfOpen)
                {
                    _probeInFlight = false;
                }
            }
        }
    }

    private void CancelPendingOpening()
    {
        _openingPending = false;
        _openingGeneration++;
    }

    private void ValidateGeneratedBreakDuration(TimeSpan duration) =>
        ConfigurationValidation.ThrowIf(
            duration <= TimeSpan.Zero,
            _optionsType,
            nameof(CircuitBreakerOptions.BreakDurationGenerator),
            duration,
            "must return a positive duration");

    private readonly record struct OpeningReservation(
        long Generation,
        long AdmissionGeneration,
        CircuitState ExpectedState,
        Exception? Exception,
        KevlarContext Context,
        CircuitBreakerFailureStatistics Statistics);

    /// <summary>Releases a half-open probe slot without recording an outcome (e.g. the probe was cancelled).</summary>
    public void AbandonProbe(long probeGeneration)
    {
        lock (_gate)
        {
            if (_state == CircuitState.HalfOpen && _admissionGeneration == probeGeneration)
            {
                _probeInFlight = false;
            }
        }
    }

    public void Isolate()
    {
        Publish(IsolateCore());
    }

    public ValueTask IsolateAsync() => PublishAsync(IsolateCore());

    private TransitionPublication? IsolateCore()
    {
        lock (_gate)
        {
            CancelPendingOpening();
            _probeInFlight = false;
            return _state == CircuitState.Isolated
                ? null
                : ChangeState(CircuitState.Isolated, KevlarContext.CreateManual());
        }
    }

    public void Reset()
    {
        Publish(ResetCore());
    }

    public ValueTask ResetAsync() => PublishAsync(ResetCore());

    private TransitionPublication? ResetCore()
    {
        lock (_gate)
        {
            CancelPendingOpening();
            Interlocked.Increment(ref _admissionGeneration);
            ResetMetrics();
            _probeInFlight = false;
            _lastException = null;
            _openTimeProvider = null;
            return _state == CircuitState.Closed
                ? null
                : ChangeState(CircuitState.Closed, KevlarContext.CreateManual());
        }
    }

    private bool RecordFailureAndCheckThreshold(
        double timestamp,
        out CircuitBreakerFailureStatistics statistics)
    {
        var consecutiveFailures = Interlocked.Increment(ref _consecutiveFailures);
        if (_consecutiveFailureLimit is { } limit)
        {
            statistics = new CircuitBreakerFailureStatistics(
                FailureRate: 1,
                FailureCount: consecutiveFailures,
                ConsecutiveFailures: consecutiveFailures);
            return consecutiveFailures >= limit;
        }

        var bucket = AdvanceBucket(timestamp);
        bucket.Failures++;

        long failures = 0, total = 0;
        for (var i = 0; i < BucketCount; i++)
        {
            var sample = _ratioBuckets[i];
            if (sample is null)
            {
                continue;
            }

            var bucketFailures = sample.Failures;
            failures += bucketFailures;
            total += bucketFailures + Interlocked.Read(ref sample.Successes);
        }

        var failureRate = (double)failures / total;
        statistics = new CircuitBreakerFailureStatistics(
            failureRate,
            failures,
            consecutiveFailures);
        return total >= _minimumThroughput && failureRate >= _failureRatio!.Value;
    }

    private RatioBucket AdvanceBucket(TimeProvider timeProvider) =>
        AdvanceBucket(GetCurrentTimestamp(timeProvider));

    private RatioBucket AdvanceBucket(double timestamp)
    {
        var currentBucket = _currentRatioBucket;
        if (double.IsNaN(_currentBucketStart))
        {
            _currentBucketStart = timestamp;
            currentBucket = new RatioBucket();
            _ratioBuckets[_currentBucketIndex] = currentBucket;
        }
        else
        {
            var elapsed = timestamp - _currentBucketStart;
            if (elapsed >= _bucketDurationTimestampUnits)
            {
                var advance = elapsed >= _bucketDurationTimestampUnits * BucketCount
                    ? BucketCount
                    : (int)(elapsed / _bucketDurationTimestampUnits);

                for (var i = 1; i <= advance; i++)
                {
                    var index = (_currentBucketIndex + i) % BucketCount;
                    _ratioBuckets[index] = null;
                }

                _currentBucketIndex = (_currentBucketIndex + advance) % BucketCount;
                _currentBucketStart = advance == BucketCount
                    ? timestamp
                    : _currentBucketStart + (advance * _bucketDurationTimestampUnits);
                currentBucket = new RatioBucket();
                _ratioBuckets[_currentBucketIndex] = currentBucket;
            }
        }

        Volatile.Write(ref _currentRatioBucket, currentBucket);
        Volatile.Write(
            ref _currentBucketEnd,
            _currentBucketStart + _bucketDurationTimestampUnits);
        return currentBucket!;
    }

    private void ResetMetrics()
    {
        Volatile.Write(ref _consecutiveFailures, 0);
        Volatile.Write(ref _currentRatioBucket, null);
        _currentBucketStart = double.NaN;
        Volatile.Write(ref _currentBucketEnd, double.NaN);
        _currentBucketIndex = 0;
        Array.Clear(_ratioBuckets, 0, BucketCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetCurrentTimestamp(TimeProvider timeProvider)
    {
        if (!ReferenceEquals(timeProvider, TimeProvider.System))
        {
            // Alternate providers share a normalized timeline protected by _gate. Once one is
            // observed, keep every provider on that path so their epochs cannot diverge.
            Volatile.Write(ref _systemRatioFastPathEnabled, 0);
        }

        var timestamp = timeProvider.GetTimestamp();
        if (!_timestampOrigins.TryGetValue(timeProvider, out var origin))
        {
            origin = new TimestampOrigin(timeProvider, timestamp, _latestTimestamp);
            _timestampOrigins.Add(timeProvider, origin);
            if (ReferenceEquals(timeProvider, TimeProvider.System))
            {
                Volatile.Write(ref _systemTimestampOrigin, origin);
            }

            return _latestTimestamp;
        }

        var elapsedTimestamp = unchecked(timestamp - origin.ProviderTimestamp);
        return UpdateTimeline(
            origin.TimelineTimestamp
            + (elapsedTimestamp * origin.TimestampScale));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double UpdateTimeline(double timestamp)
    {
        _latestTimestamp = Math.Max(_latestTimestamp, timestamp);
        return _latestTimestamp;
    }

    private CircuitState GetReportedState(TimeProvider? timeProvider) =>
        _state == CircuitState.Open
        && timeProvider is not null
        && GetCurrentTimestamp(timeProvider) >= _openUntilTimestamp
            ? CircuitState.HalfOpen
            : _state;

    private static TimeSpan GetElapsedTime(double timestampUnits)
    {
        var ticks = Math.Max(0, timestampUnits) * SecondsPerSystemTimestamp * TimeSpan.TicksPerSecond;
        return ticks >= TimeSpan.MaxValue.Ticks
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks((long)ticks);
    }

    private sealed class TimestampOrigin
    {
        public TimestampOrigin(
            TimeProvider timeProvider,
            long providerTimestamp,
            double timelineTimestamp)
        {
            ProviderTimestamp = providerTimestamp;
            TimelineTimestamp = timelineTimestamp;
            TimestampScale = Stopwatch.Frequency / (double)timeProvider.TimestampFrequency;
        }

        public long ProviderTimestamp { get; }

        public double TimelineTimestamp { get; }

        public double TimestampScale { get; }
    }

    private sealed class RatioBucket
    {
        public long Failures;

        public long Successes;
    }

    private TransitionPublication ChangeState(
        CircuitState next,
        KevlarContext context,
        TimeSpan? breakDuration = null)
    {
        var transition = new CircuitBreakerStateChangedEvent(
            _state,
            next,
            _lastException,
            context,
            breakDuration ?? (next == CircuitState.Open ? _breakDuration : default));
        _state = next;
        if (next is CircuitState.Open or CircuitState.Isolated)
        {
            Interlocked.Increment(ref _admissionGeneration);
        }
        else if (next == CircuitState.Closed)
        {
            _lastException = null;
            _openTimeProvider = null;
        }
        var publication = new TransitionPublication(transition);
        _pendingTransitions.Enqueue(publication);
        if (!_isPublishing)
        {
            _isPublishing = true;
            publication.StartsDrain = true;
        }

        return publication;
    }

    private void Publish(TransitionPublication? publication)
    {
        if (_onStateChanged is null)
        {
            PublishSynchronously(publication);
            return;
        }

        PublishOnThreadPool(publication);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void PublishOnThreadPool(TransitionPublication? publication)
    {
        var parent = _ambientPublication!.Value;
        Task.Run(() => PublishAsync(publication, parent).AsTask()).GetAwaiter().GetResult();
    }

    private ValueTask PublishAsync(TransitionPublication? publication)
        => PublishAsync(publication, _ambientPublication?.Value);

    private ValueTask PublishAsync(
        TransitionPublication? publication,
        TransitionPublication? parent)
    {
        if (publication is null)
        {
            return default;
        }

        if (_onStateChanged is null)
        {
            PublishSynchronously(publication);
            return default;
        }

        if (publication.StartsDrain)
        {
            return DrainPublicationsAsync(publication);
        }

        if (parent is not null)
        {
            lock (_gate)
            {
                if (!parent.ObserversCompleted)
                {
                    publication.DetachContext();
                    publication.Parent = parent;
                    parent.PendingChildren++;
                    return default;
                }
            }
        }

        return WaitForPublicationAsync(publication);
    }

    private void PublishSynchronously(TransitionPublication? publication)
    {
        if (publication is null)
        {
            return;
        }

        if (publication.StartsDrain)
        {
            DrainPublications();
            publication.ThrowIfFailed();
        }
        else if (Volatile.Read(ref _publishingThreadId) == Environment.CurrentManagedThreadId)
        {
            publication.DetachContext();
            publication.Parent = _activePublication;
            _activePublication!.PendingChildren++;
        }
        else
        {
            publication.Completion.Task.GetAwaiter().GetResult();
            publication.ThrowIfFailed();
        }
    }

    private async ValueTask WaitForPublicationAsync(TransitionPublication publication)
    {
        await publication.Completion.Task.ConfigureAwait(false);
        publication.ThrowIfFailed();
    }

    private void DrainPublications()
    {
        lock (_gate)
        {
            Volatile.Write(ref _publishingThreadId, Environment.CurrentManagedThreadId);
        }

        TransitionPublication? activePublication = null;
        Exception? drainFailure = null;
        var completed = false;
        try
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_pendingTransitions.Count == 0)
                    {
                        Volatile.Write(ref _publishingThreadId, 0);
                        _isPublishing = false;
                        completed = true;
                        return;
                    }

                    activePublication = _pendingTransitions.Dequeue();
                }

                _activePublication = activePublication;
                activePublication.Failure = PublishObservers(activePublication.StateChange);
                _activePublication = null;
                activePublication.ObserversCompleted = true;
                CompletePublication(activePublication);
                activePublication = null;
            }
        }
        catch (Exception exception)
        {
            drainFailure = exception;
            throw;
        }
        finally
        {
            if (!completed)
            {
                lock (_gate)
                {
                    _activePublication = null;
                    Volatile.Write(ref _publishingThreadId, 0);
                    _isPublishing = false;
                    FailPublication(activePublication, drainFailure);
                    while (_pendingTransitions.TryDequeue(out var publication))
                    {
                        FailPublication(publication, drainFailure);
                    }
                }
            }
        }
    }

    private async ValueTask DrainPublicationsAsync(TransitionPublication starter)
    {
        TransitionPublication? activePublication = null;
        Exception? drainFailure = null;
        var completed = false;
        try
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_pendingTransitions.Count == 0)
                    {
                        _isPublishing = false;
                        completed = true;
                        break;
                    }

                    activePublication = _pendingTransitions.Dequeue();
                }

                var previousPublication = _ambientPublication!.Value;
                _ambientPublication.Value = activePublication;
                try
                {
                    activePublication.Failure = await PublishObserversAsync(
                        activePublication.StateChange).ConfigureAwait(false);
                }
                finally
                {
                    _ambientPublication.Value = previousPublication;
                }

                lock (_gate)
                {
                    activePublication.ObserversCompleted = true;
                    CompletePublication(activePublication);
                }

                activePublication = null;
            }
        }
        catch (Exception exception)
        {
            drainFailure = exception;
            throw;
        }
        finally
        {
            if (!completed)
            {
                lock (_gate)
                {
                    _isPublishing = false;
                    FailPublication(activePublication, drainFailure);
                    while (_pendingTransitions.TryDequeue(out var publication))
                    {
                        FailPublication(publication, drainFailure);
                    }
                }
            }
        }

        starter.ThrowIfFailed();
    }

    private static void CompletePublication(TransitionPublication publication)
    {
        if (!publication.ObserversCompleted || publication.PendingChildren != 0)
        {
            return;
        }

        publication.Completion.TrySetResult(true);
        if (publication.Parent is not { } parent)
        {
            return;
        }

        if (publication.Failure is { } failure)
        {
            var parentFailure = parent.Failure;
            AddFailure(ref parentFailure, failure);
            parent.Failure = parentFailure;
        }

        parent.PendingChildren--;
        CompletePublication(parent);
    }

    private static void FailPublication(TransitionPublication? publication, Exception? failure)
    {
        if (publication is null)
        {
            return;
        }

        if (failure is not null)
        {
            var publicationFailure = publication.Failure;
            AddFailure(ref publicationFailure, failure);
            publication.Failure = publicationFailure;
        }

        publication.ObserversCompleted = true;
        CompletePublication(publication);
    }

    private Exception? PublishObservers(CircuitBreakerStateChangedEvent stateChange)
    {
        Exception? failure = null;
        try
        {
            KevlarMetrics.CircuitTransition(stateChange.From, stateChange.To);
            RecordTelemetry(stateChange);
        }
        catch (Exception exception)
        {
            AddFailure(ref failure, exception);
        }

        try
        {
            _recordState(stateChange.To);
        }
        catch (Exception exception)
        {
            AddFailure(ref failure, exception);
        }

        _monitor?.Raise(in stateChange);

        return failure;
    }

    private async ValueTask<Exception?> PublishObserversAsync(
        CircuitBreakerStateChangedEvent stateChange)
    {
        Exception? failure = null;
        try
        {
            KevlarMetrics.CircuitTransition(stateChange.From, stateChange.To);
            RecordTelemetry(stateChange);
        }
        catch (Exception exception)
        {
            AddFailure(ref failure, exception);
        }

        try
        {
            _recordState(stateChange.To);
        }
        catch (Exception exception)
        {
            AddFailure(ref failure, exception);
        }

        try
        {
            await CallbackInvoker.InvokeAsync(
                _onStateChanged,
                stateChange,
                CallbackErrorKind.CircuitStateChanged,
                stateChange.Context,
                "CircuitBreakerOptions.OnStateChanged").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Only the synchronous-execution guard escapes CallbackInvoker; surface it to the
            // caller through the publication failure so the drain state stays consistent.
            AddFailure(ref failure, exception);
        }

        _monitor?.Raise(in stateChange);

        return failure;
    }

    private void RecordTelemetry(CircuitBreakerStateChangedEvent stateChange)
    {
        var context = stateChange.Context;
        KevlarTelemetry.Record(
            context,
            _telemetryName,
            TelemetryEventName(stateChange.To),
            TelemetrySeverity(stateChange.To),
            context.StrategyIndex,
            context.AttemptNumber,
            isSuccess: stateChange.To == CircuitState.Closed,
            stateChange.LastException,
            delay: stateChange.BreakDuration,
            fromState: stateChange.From,
            toState: stateChange.To);

        if (context.TelemetryListener is null)
        {
            RecordAttachedTelemetry(stateChange);
        }
    }

    private void RecordAttachedTelemetry(CircuitBreakerStateChangedEvent stateChange)
    {
        var registrations = GetLiveTelemetryRegistrations();
        var context = stateChange.Context;
        var previousShieldName = context.ShieldName;
        var previousStrategyIndex = context.StrategyIndex;
        try
        {
            foreach (var registration in registrations)
            {
                if (!registration.Listener.TryGetTarget(out var listener)
                    || registration.ScopeOwner is { } owner
                        && !owner.TryGetTarget(out _))
                {
                    continue;
                }

                context.ShieldName = registration.ShieldName;
                context.StrategyIndex = registration.StrategyIndex;
                context.TelemetryListener = listener;
                KevlarTelemetry.Record(
                    context,
                    _telemetryName,
                    TelemetryEventName(stateChange.To),
                    TelemetrySeverity(stateChange.To),
                    registration.StrategyIndex,
                    context.AttemptNumber,
                    isSuccess: stateChange.To == CircuitState.Closed,
                    stateChange.LastException,
                    delay: stateChange.BreakDuration,
                    fromState: stateChange.From,
                    toState: stateChange.To,
                    localOnly: true);
            }
        }
        finally
        {
            context.ShieldName = previousShieldName;
            context.StrategyIndex = previousStrategyIndex;
            context.TelemetryListener = null;
        }
    }

    private CircuitTelemetryRegistration[] GetLiveTelemetryRegistrations()
    {
        var registrations = Volatile.Read(ref _telemetryRegistrations);
        var hasExpiredRegistration = false;
        foreach (var registration in registrations)
        {
            if (!registration.IsAlive)
            {
                hasExpiredRegistration = true;
                break;
            }
        }

        if (!hasExpiredRegistration)
        {
            return registrations;
        }

        lock (_telemetryGate)
        {
            registrations = _telemetryRegistrations;
            var live = new List<CircuitTelemetryRegistration>(registrations.Length);
            foreach (var registration in registrations)
            {
                if (registration.IsAlive)
                {
                    live.Add(registration);
                }
            }

            var result = live.ToArray();
            Volatile.Write(ref _telemetryRegistrations, result);
            return result;
        }
    }

    private static string TelemetryEventName(CircuitState state) => state switch
    {
        CircuitState.Open => "circuit_opened",
        CircuitState.HalfOpen => "circuit_half_opened",
        CircuitState.Closed => "circuit_closed",
        CircuitState.Isolated => "circuit_isolated",
        _ => "circuit_changed",
    };

    private static KevlarTelemetrySeverity TelemetrySeverity(CircuitState state) =>
        state is CircuitState.Open or CircuitState.Isolated
            ? KevlarTelemetrySeverity.Warning
            : KevlarTelemetrySeverity.Information;

    private readonly record struct CircuitTelemetryRegistration(
        WeakReference<IKevlarTelemetryListener> Listener,
        string? ShieldName,
        int StrategyIndex,
        WeakReference<object>? ScopeOwner)
    {
        public CircuitTelemetryRegistration(
            IKevlarTelemetryListener listener,
            string? shieldName,
            int strategyIndex,
            object? scopeOwner)
            : this(
                new WeakReference<IKevlarTelemetryListener>(listener),
                shieldName,
                strategyIndex,
                scopeOwner is null ? null : new WeakReference<object>(scopeOwner))
        {
        }

        public bool HasScopeOwner(object? scopeOwner) =>
            ScopeOwner is null
                ? scopeOwner is null
                : scopeOwner is not null
                    && ScopeOwner.TryGetTarget(out var registeredOwner)
                    && ReferenceEquals(registeredOwner, scopeOwner);

        public bool IsAlive =>
            Listener.TryGetTarget(out _)
            && (ScopeOwner is null || ScopeOwner.TryGetTarget(out _));
    }

    private static void AddFailure(ref Exception? failure, Exception next)
    {
        failure = failure switch
        {
            null => next,
            AggregateException aggregate => new AggregateException([.. aggregate.InnerExceptions, next]),
            _ => new AggregateException(failure, next),
        };
    }

    private sealed class TransitionPublication(CircuitBreakerStateChangedEvent stateChange)
    {
        public CircuitBreakerStateChangedEvent StateChange { get; private set; } = stateChange;

        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool StartsDrain { get; set; }

        public TransitionPublication? Parent { get; set; }

        public int PendingChildren { get; set; }

        public bool ObserversCompleted { get; set; }

        public Exception? Failure { get; set; }

        public void DetachContext() => StateChange = StateChange.WithDetachedContext();

        public void ThrowIfFailed()
        {
            if (Failure is { } failure)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }
}
