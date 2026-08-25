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
    private readonly Action<CircuitBreakerStateChangedEvent>? _onStateChanged;
    private readonly Func<CircuitBreakerStateChangedEvent, ValueTask>? _onStateChangedAsync;
    private readonly CircuitBreakerMonitor? _monitor;
    private readonly Type _optionsType;
    private readonly string _telemetryName;
    private readonly Queue<TransitionPublication> _pendingTransitions = new();
    private readonly AsyncLocal<TransitionPublication?>? _ambientPublication;

    private readonly long[] _bucketFailures = new long[BucketCount];
    private readonly long[] _bucketSuccesses = new long[BucketCount];
    private double _currentBucketStart = double.NaN;
    private int _currentBucketIndex;

    private CircuitState _state = CircuitState.Closed;
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
        int strategyIndex)
    {
        lock (_telemetryGate)
        {
            var registrations = _telemetryRegistrations;
            if (previous is not null)
            {
                for (var index = 0; index < registrations.Length; index++)
                {
                    if (registrations[index].Listener.TryGetTarget(out var registered)
                        && ReferenceEquals(registered, previous))
                    {
                        var replacement = (CircuitTelemetryRegistration[])registrations.Clone();
                        replacement[index] = new CircuitTelemetryRegistration(
                            listener,
                            shieldName,
                            strategyIndex);
                        Volatile.Write(ref _telemetryRegistrations, replacement);
                        return;
                    }
                }
            }

            var updated = new CircuitTelemetryRegistration[registrations.Length + 1];
            Array.Copy(registrations, updated, registrations.Length);
            updated[^1] = new CircuitTelemetryRegistration(listener, shieldName, strategyIndex);
            Volatile.Write(ref _telemetryRegistrations, updated);
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
        _onStateChangedAsync = options.OnStateChangedAsync;
        _optionsType = optionsType;
        _telemetryName = options.Name ?? "CircuitBreaker";
        _ambientPublication = options.OnStateChangedAsync is null
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

    public bool RequiresAsyncExecution => _breakDurationGenerator is not null || _onStateChangedAsync is not null;

    internal string TelemetryName => _telemetryName;

    public string? SynchronousExecutionUnsupportedReason =>
        _breakDurationGenerator?.IsAsynchronous == true
            ? "CircuitBreakerOptions.BreakDurationGenerator"
            : _onStateChangedAsync is not null
                ? "CircuitBreakerOptions.OnStateChangedAsync"
                : null;

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
        Publish(RecordSuccessCore(timeProvider, context, admissionGeneration));
    }

    public ValueTask RecordSuccessAsync(
        TimeProvider timeProvider,
        KevlarContext context,
        long admissionGeneration) =>
        PublishAsync(RecordSuccessCore(timeProvider, context, admissionGeneration));

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
                _consecutiveFailures = 0;
                if (_failureRatio is not null)
                {
                    _bucketSuccesses[AdvanceBucket(timeProvider)]++;
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
            return ChangeState(CircuitState.Open, reservation.Context);
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
            _admissionGeneration++;
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
        _consecutiveFailures++;
        if (_consecutiveFailureLimit is { } limit)
        {
            statistics = new CircuitBreakerFailureStatistics(
                FailureRate: 1,
                FailureCount: _consecutiveFailures,
                ConsecutiveFailures: _consecutiveFailures);
            return _consecutiveFailures >= limit;
        }

        var bucket = AdvanceBucket(timestamp);
        _bucketFailures[bucket]++;

        long failures = 0, total = 0;
        for (var i = 0; i < BucketCount; i++)
        {
            failures += _bucketFailures[i];
            total += _bucketFailures[i] + _bucketSuccesses[i];
        }

        var failureRate = (double)failures / total;
        statistics = new CircuitBreakerFailureStatistics(
            failureRate,
            failures,
            _consecutiveFailures);
        return total >= _minimumThroughput && failureRate >= _failureRatio!.Value;
    }

    private int AdvanceBucket(TimeProvider timeProvider) => AdvanceBucket(GetCurrentTimestamp(timeProvider));

    private int AdvanceBucket(double timestamp)
    {
        if (double.IsNaN(_currentBucketStart))
        {
            _currentBucketStart = timestamp;
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
                    _bucketFailures[index] = 0;
                    _bucketSuccesses[index] = 0;
                }

                _currentBucketIndex = (_currentBucketIndex + advance) % BucketCount;
                _currentBucketStart = advance == BucketCount
                    ? timestamp
                    : _currentBucketStart + (advance * _bucketDurationTimestampUnits);
            }
        }

        return _currentBucketIndex;
    }

    private void ResetMetrics()
    {
        _consecutiveFailures = 0;
        _currentBucketStart = double.NaN;
        _currentBucketIndex = 0;
        Array.Clear(_bucketFailures, 0, BucketCount);
        Array.Clear(_bucketSuccesses, 0, BucketCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetCurrentTimestamp(TimeProvider timeProvider)
    {
        var timestamp = timeProvider.GetTimestamp();
        if (!_timestampOrigins.TryGetValue(timeProvider, out var origin))
        {
            origin = new TimestampOrigin(timeProvider, timestamp, _latestTimestamp);
            _timestampOrigins.Add(timeProvider, origin);
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

    private TransitionPublication ChangeState(CircuitState next, KevlarContext context)
    {
        var transition = new CircuitBreakerStateChangedEvent(
            _state,
            next,
            _lastException,
            context);
        _state = next;
        if (next is CircuitState.Open or CircuitState.Isolated)
        {
            _admissionGeneration++;
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
        if (_onStateChangedAsync is null)
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

        if (_onStateChangedAsync is null)
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

        CallbackInvoker.Invoke(
            _onStateChanged,
            stateChange,
            CallbackErrorKind.CircuitStateChanged,
            stateChange.Context);
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

        CallbackInvoker.Invoke(
            _onStateChanged,
            stateChange,
            CallbackErrorKind.CircuitStateChanged,
            stateChange.Context);
        await CallbackInvoker.InvokeAsync(
            _onStateChangedAsync,
            stateChange,
            CallbackErrorKind.CircuitStateChanged,
            stateChange.Context).ConfigureAwait(false);
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
            delay: stateChange.To == CircuitState.Open ? _breakDuration : default,
            fromState: stateChange.From,
            toState: stateChange.To);

        if (context.TelemetryListener is null)
        {
            RecordAttachedTelemetry(stateChange);
        }
    }

    private void RecordAttachedTelemetry(CircuitBreakerStateChangedEvent stateChange)
    {
        var registrations = Volatile.Read(ref _telemetryRegistrations);
        var context = stateChange.Context;
        var previousShieldName = context.ShieldName;
        var previousStrategyIndex = context.StrategyIndex;
        try
        {
            foreach (var registration in registrations)
            {
                if (!registration.Listener.TryGetTarget(out var listener))
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
                    delay: stateChange.To == CircuitState.Open ? _breakDuration : default,
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
        int StrategyIndex)
    {
        public CircuitTelemetryRegistration(
            IKevlarTelemetryListener listener,
            string? shieldName,
            int strategyIndex)
            : this(new WeakReference<IKevlarTelemetryListener>(listener), shieldName, strategyIndex)
        {
        }
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
