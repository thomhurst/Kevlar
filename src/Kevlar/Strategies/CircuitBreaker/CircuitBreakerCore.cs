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
    private readonly ConditionalWeakTable<TimeProvider, TimestampOrigin> _timestampOrigins = new();
    private readonly int? _consecutiveFailureLimit;
    private readonly double? _failureRatio;
    private readonly int _minimumThroughput;
    private readonly TimeSpan _samplingWindow;
    private readonly double _bucketDurationTimestampUnits;
    private readonly TimeSpan _breakDuration;
    private readonly double _breakDurationTimestampUnits;
    private readonly Action<CircuitState> _recordState;
    private readonly Func<CircuitBreakerBreakDurationEvent, ValueTask<TimeSpan>>? _breakDurationGenerator;
    private readonly Action<CircuitStateChangedEvent>? _onStateChanged;
    private readonly Func<CircuitStateChangedEvent, ValueTask>? _onStateChangedAsync;
    private readonly CircuitBreakerMonitor? _monitor;
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
    private long _probeGeneration;
    private long _activeProbeGeneration;
    private Exception? _lastException;
    private long _openingGeneration;
    private bool _openingPending;
    private bool _isPublishing;
    private int _publishingThreadId;
    private TransitionPublication? _activePublication;

    public CircuitBreakerCore(CircuitBreakerOptions options, Action<CircuitState> recordState)
    {
        Throw.IfOutOfRange(options.ConsecutiveFailures is <= 0, nameof(options), "ConsecutiveFailures must be positive.");
        Throw.IfOutOfRange(
            options.FailureRatio is { } ratio && (double.IsNaN(ratio) || ratio <= 0 || ratio > 1),
            nameof(options.FailureRatio),
            "FailureRatio must be between 0 (exclusive) and 1 (inclusive).");
        Throw.IfOutOfRange(options.ConsecutiveFailures is not null && options.FailureRatio is not null, nameof(options), "Configure either ConsecutiveFailures or FailureRatio, not both.");
        Throw.IfOutOfRange(options.MinimumThroughput < 1, nameof(options), "MinimumThroughput must be at least 1.");
        Throw.IfOutOfRange(options.SamplingWindow <= TimeSpan.Zero, nameof(options), "SamplingWindow must be positive.");
        Throw.IfOutOfRange(options.BreakDuration <= TimeSpan.Zero, nameof(options), "BreakDuration must be positive.");

        _failureRatio = options.FailureRatio;
        _consecutiveFailureLimit = options.FailureRatio is null ? options.ConsecutiveFailures ?? 5 : null;
        _samplingWindow = options.SamplingWindow;
        _minimumThroughput = options.MinimumThroughput;
        _bucketDurationTimestampUnits = options.SamplingWindow.TotalSeconds * Stopwatch.Frequency / BucketCount;
        _breakDuration = options.BreakDuration;
        _breakDurationTimestampUnits = options.BreakDuration.TotalSeconds * Stopwatch.Frequency;
        _recordState = recordState;
        _breakDurationGenerator = options.BreakDurationGenerator;
        _onStateChanged = options.OnStateChanged;
        _onStateChangedAsync = options.OnStateChangedAsync;
        _ambientPublication = options.OnStateChangedAsync is null
            ? null
            : new AsyncLocal<TransitionPublication?>();
        _monitor = options.Monitor;
        _monitor?.Bind(this);
    }

    public string Describe() =>
        _consecutiveFailureLimit is { } limit
            ? $"CircuitBreaker({limit} consecutive, break {DescribeBreakDuration()})"
            : FormattableString.Invariant(
                $"CircuitBreaker({_failureRatio!.Value * 100:0.#}% over {DescribeHelper.Time(_samplingWindow)}, min {_minimumThroughput}, break {DescribeBreakDuration()})");

    public bool RequiresAsyncExecution => _breakDurationGenerator is not null || _onStateChangedAsync is not null;

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
                return _state;
            }
        }
    }

    /// <summary>
    /// Gates an execution. Returns <see langword="false"/> with a rejection when the circuit
    /// refuses it; a <see langword="true"/> return during half-open marks a probe in flight,
    /// so the caller must report back via Record* or <see cref="AbandonProbe(long)"/>.
    /// </summary>
    public bool TryEnter(
        TimeProvider timeProvider,
        out CircuitOpenException? rejection,
        out long admittedProbeGeneration)
    {
        var allowed = TryEnterCore(timeProvider, out rejection, out var transition, out admittedProbeGeneration);

        try
        {
            Publish(transition);
        }
        catch
        {
            if (transition?.StateChange.To == CircuitState.HalfOpen)
            {
                AbandonProbe(admittedProbeGeneration);
            }

            throw;
        }

        return allowed;
    }

    public ValueTask<EntryResult> TryEnterAsync(TimeProvider timeProvider)
    {
        var allowed = TryEnterCore(
            timeProvider,
            out var rejection,
            out var transition,
            out var admittedProbeGeneration);
        ValueTask publication;
        try
        {
            publication = PublishAsync(transition);
        }
        catch
        {
            if (transition?.StateChange.To == CircuitState.HalfOpen)
            {
                AbandonProbe(admittedProbeGeneration);
            }

            throw;
        }

        if (publication.IsCompletedSuccessfully)
        {
            publication.GetAwaiter().GetResult();
            return new ValueTask<EntryResult>(new EntryResult(allowed, rejection, admittedProbeGeneration));
        }

        return AwaitEntryPublicationAsync(
            publication,
            allowed,
            rejection,
            transition,
            admittedProbeGeneration);
    }

    private bool TryEnterCore(
        TimeProvider timeProvider,
        out CircuitOpenException? rejection,
        out TransitionPublication? transition,
        out long admittedProbeGeneration)
    {
        transition = null;
        admittedProbeGeneration = 0;
        rejection = null;

        lock (_gate)
        {
            switch (_state)
            {
                case CircuitState.Closed:
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

                    transition = ChangeState(CircuitState.HalfOpen);
                    _probeInFlight = true;
                    admittedProbeGeneration = _activeProbeGeneration = ++_probeGeneration;
                    return true;

                default: // HalfOpen
                    if (_probeInFlight)
                    {
                        rejection = new CircuitOpenException(null, isIsolated: false, _lastException);
                        return false;
                    }

                    _probeInFlight = true;
                    admittedProbeGeneration = _activeProbeGeneration = ++_probeGeneration;
                    return true;
            }
        }
    }

    private async ValueTask<EntryResult> AwaitEntryPublicationAsync(
        ValueTask publication,
        bool allowed,
        CircuitOpenException? rejection,
        TransitionPublication? transition,
        long admittedProbeGeneration)
    {
        try
        {
            await publication.ConfigureAwait(false);
            return new EntryResult(allowed, rejection, admittedProbeGeneration);
        }
        catch
        {
            if (transition?.StateChange.To == CircuitState.HalfOpen)
            {
                AbandonProbe(admittedProbeGeneration);
            }

            throw;
        }
    }

    public readonly record struct EntryResult(
        bool Allowed,
        CircuitOpenException? Rejection,
        long AdmittedProbeGeneration);

    public void RecordSuccess(TimeProvider timeProvider)
    {
        Publish(RecordSuccessCore(timeProvider));
    }

    public ValueTask RecordSuccessAsync(TimeProvider timeProvider) =>
        PublishAsync(RecordSuccessCore(timeProvider));

    private TransitionPublication? RecordSuccessCore(TimeProvider timeProvider)
    {
        lock (_gate)
        {
            if (_state == CircuitState.HalfOpen)
            {
                _probeInFlight = false;
                CancelPendingOpening();
                ResetMetrics();
                return ChangeState(CircuitState.Closed);
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

    public void RecordFailure(TimeProvider timeProvider, Exception? exception)
    {
        Publish(RecordFailureCore(timeProvider, exception));
    }

    public ValueTask RecordFailureAsync<T>(
        TimeProvider timeProvider,
        in Outcome<T> outcome,
        KevlarContext context)
    {
        if (_breakDurationGenerator is null)
        {
            return PublishAsync(RecordFailureCore(timeProvider, outcome.Exception));
        }

        if (!TryReserveDynamicOpening(timeProvider, outcome.Exception, out var reservation))
        {
            return default;
        }

        ValueTask<TimeSpan> generation;
        try
        {
            var item = new CircuitBreakerBreakDurationEvent(
                outcome.Exception,
                outcome.Exception is null ? outcome.Result : null,
                context);
            generation = _breakDurationGenerator(item);
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

    private TransitionPublication? RecordFailureCore(TimeProvider timeProvider, Exception? exception)
    {
        lock (_gate)
        {
            _lastException = exception;

            if (_state == CircuitState.HalfOpen)
            {
                _probeInFlight = false;
                _openUntilTimestamp = GetCurrentTimestamp(timeProvider) + _breakDurationTimestampUnits;
                return ChangeState(CircuitState.Open);
            }

            if (_state == CircuitState.Closed)
            {
                var timestamp = _failureRatio is null
                    ? 0
                    : GetCurrentTimestamp(timeProvider);

                if (IsTripped(timestamp))
                {
                    if (_failureRatio is null)
                    {
                        timestamp = GetCurrentTimestamp(timeProvider);
                    }

                    _openUntilTimestamp = timestamp + _breakDurationTimestampUnits;
                    return ChangeState(CircuitState.Open);
                }
            }

            return null;
        }
    }

    private bool TryReserveDynamicOpening(
        TimeProvider timeProvider,
        Exception? exception,
        out OpeningReservation reservation)
    {
        lock (_gate)
        {
            reservation = default;
            _lastException = exception;
            if (_openingPending)
            {
                if (_state == CircuitState.Closed)
                {
                    _ = IsTripped(_failureRatio is null ? 0 : GetCurrentTimestamp(timeProvider));
                }

                return false;
            }

            var shouldOpen = _state switch
            {
                CircuitState.HalfOpen => true,
                CircuitState.Closed => IsTripped(
                    _failureRatio is null ? 0 : GetCurrentTimestamp(timeProvider)),
                _ => false,
            };
            if (!shouldOpen)
            {
                return false;
            }

            _openingPending = true;
            reservation = new OpeningReservation(++_openingGeneration, _state, exception);
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
                || _state != reservation.ExpectedState)
            {
                return null;
            }

            _openingPending = false;
            _probeInFlight = false;
            _lastException = reservation.Exception;
            _openUntilTimestamp = GetCurrentTimestamp(timeProvider)
                + (duration.TotalSeconds * Stopwatch.Frequency);
            return ChangeState(CircuitState.Open);
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

    private static void ValidateGeneratedBreakDuration(TimeSpan duration) =>
        Throw.IfOutOfRange(
            duration <= TimeSpan.Zero,
            nameof(duration),
            "Generated break duration must be positive.");

    private readonly record struct OpeningReservation(
        long Generation,
        CircuitState ExpectedState,
        Exception? Exception);

    /// <summary>Releases a half-open probe slot without recording an outcome (e.g. the probe was cancelled).</summary>
    public void AbandonProbe(long probeGeneration)
    {
        lock (_gate)
        {
            if (_state == CircuitState.HalfOpen && _activeProbeGeneration == probeGeneration)
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
                : ChangeState(CircuitState.Isolated);
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
            ResetMetrics();
            _probeInFlight = false;
            return _state == CircuitState.Closed
                ? null
                : ChangeState(CircuitState.Closed);
        }
    }

    private bool IsTripped(double timestamp)
    {
        if (_consecutiveFailureLimit is { } limit)
        {
            return ++_consecutiveFailures >= limit;
        }

        var bucket = AdvanceBucket(timestamp);
        _bucketFailures[bucket]++;

        long failures = 0, total = 0;
        for (var i = 0; i < BucketCount; i++)
        {
            failures += _bucketFailures[i];
            total += _bucketFailures[i] + _bucketSuccesses[i];
        }

        return total >= _minimumThroughput && (double)failures / total >= _failureRatio!.Value;
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

    private TransitionPublication ChangeState(CircuitState next)
    {
        var transition = new CircuitStateChangedEvent(_state, next, _lastException);
        _state = next;
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

        PublishAsync(publication).AsTask().GetAwaiter().GetResult();
    }

    private ValueTask PublishAsync(TransitionPublication? publication)
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

        var parent = _ambientPublication!.Value;
        if (parent is not null)
        {
            lock (_gate)
            {
                if (!parent.ObserversCompleted)
                {
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

    private Exception? PublishObservers(CircuitStateChangedEvent stateChange)
    {
        Exception? failure = null;
        try
        {
            KevlarMetrics.CircuitTransition(stateChange.From, stateChange.To);
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
            _onStateChanged?.Invoke(stateChange);
        }
        catch (Exception exception)
        {
            AddFailure(ref failure, exception);
        }

        try
        {
            _monitor?.Raise(in stateChange);
        }
        catch (Exception monitorFailure)
        {
            AddFailure(ref failure, monitorFailure);
        }

        return failure;
    }

    private async ValueTask<Exception?> PublishObserversAsync(CircuitStateChangedEvent stateChange)
    {
        Exception? failure = null;
        try
        {
            KevlarMetrics.CircuitTransition(stateChange.From, stateChange.To);
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
            _onStateChanged?.Invoke(stateChange);
        }
        catch (Exception exception)
        {
            AddFailure(ref failure, exception);
        }

        try
        {
            var notification = _onStateChangedAsync!(stateChange);
            if (!notification.IsCompletedSuccessfully)
            {
                await notification.ConfigureAwait(false);
            }
            else
            {
                notification.GetAwaiter().GetResult();
            }
        }
        catch (Exception exception)
        {
            AddFailure(ref failure, exception);
        }

        try
        {
            _monitor?.Raise(in stateChange);
        }
        catch (Exception monitorFailure)
        {
            AddFailure(ref failure, monitorFailure);
        }

        return failure;
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

    private sealed class TransitionPublication(CircuitStateChangedEvent stateChange)
    {
        public CircuitStateChangedEvent StateChange { get; } = stateChange;

        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool StartsDrain { get; set; }

        public TransitionPublication? Parent { get; set; }

        public int PendingChildren { get; set; }

        public bool ObserversCompleted { get; set; }

        public Exception? Failure { get; set; }

        public void ThrowIfFailed()
        {
            if (Failure is { } failure)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }
}
