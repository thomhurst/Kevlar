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
    private readonly Action<CircuitStateChangedEvent>? _onStateChanged;
    private readonly CircuitBreakerMonitor? _monitor;
    private readonly Queue<TransitionPublication> _pendingTransitions = new();

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
    private bool _isPublishing;
    private int _publishingThreadId;
    private TransitionPublication? _activePublication;
    private string? _metricsShieldName;

    public CircuitBreakerCore(CircuitBreakerOptions options)
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
        _onStateChanged = options.OnStateChanged;
        _monitor = options.Monitor;
        _monitor?.Bind(this);
    }

    public string Describe() =>
        _consecutiveFailureLimit is { } limit
            ? $"CircuitBreaker({limit} consecutive, break {DescribeHelper.Time(_breakDuration)})"
            : FormattableString.Invariant(
                $"CircuitBreaker({_failureRatio!.Value * 100:0.#}% over {DescribeHelper.Time(_samplingWindow)}, min {_minimumThroughput}, break {DescribeHelper.Time(_breakDuration)})");

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

    public void SetMetricsShieldName(string? shieldName) =>
        Volatile.Write(ref _metricsShieldName, shieldName);

    /// <summary>
    /// Gates an execution. Returns <see langword="false"/> with a rejection when the circuit
    /// refuses it; a <see langword="true"/> return during half-open marks a probe in flight,
    /// so the caller must report back via Record* or <see cref="AbandonProbe()"/>.
    /// </summary>
    public bool TryEnter(TimeProvider timeProvider, out CircuitOpenException? rejection)
    {
        TransitionPublication? transition = null;
        long admittedProbeGeneration = 0;
        bool allowed;
        rejection = null;

        lock (_gate)
        {
            var now = _state == CircuitState.Open ? timeProvider.GetUtcNow() : default;
            switch (_state)
            {
                case CircuitState.Closed:
                    allowed = true;
                    break;

                case CircuitState.Isolated:
                    rejection = new CircuitOpenException(null, isIsolated: true, _lastException);
                    allowed = false;
                    break;

                case CircuitState.Open:
                    var timestamp = GetCurrentTimestamp(timeProvider);
                    if (timestamp >= _openUntilTimestamp)
                    {
                        transition = ChangeState(CircuitState.HalfOpen);
                        _probeInFlight = true;
                        admittedProbeGeneration = _activeProbeGeneration = ++_probeGeneration;
                        allowed = true;
                    }
                    else
                    {
                        rejection = new CircuitOpenException(
                            GetElapsedTime(_openUntilTimestamp - timestamp),
                            isIsolated: false,
                            _lastException);
                        allowed = false;
                    }

                    break;

                default: // HalfOpen
                    if (_probeInFlight)
                    {
                        rejection = new CircuitOpenException(null, isIsolated: false, _lastException);
                        allowed = false;
                    }
                    else
                    {
                        _probeInFlight = true;
                        admittedProbeGeneration = _activeProbeGeneration = ++_probeGeneration;
                        allowed = true;
                    }

                    break;
            }
        }

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

    public void RecordSuccess(TimeProvider timeProvider)
    {
        TransitionPublication? transition = null;

        lock (_gate)
        {
            if (_state == CircuitState.HalfOpen)
            {
                _probeInFlight = false;
                ResetMetrics();
                transition = ChangeState(CircuitState.Closed);
            }
            else if (_state == CircuitState.Closed)
            {
                _consecutiveFailures = 0;
                if (_failureRatio is not null)
                {
                    _bucketSuccesses[AdvanceBucket(timeProvider)]++;
                }
            }
        }

        Publish(transition);
    }

    public void RecordFailure(TimeProvider timeProvider, Exception? exception)
    {
        TransitionPublication? transition = null;

        lock (_gate)
        {
            _lastException = exception;

            if (_state == CircuitState.HalfOpen)
            {
                _probeInFlight = false;
                _openUntilTimestamp = GetCurrentTimestamp(timeProvider) + _breakDurationTimestampUnits;
                transition = ChangeState(CircuitState.Open);
            }
            else if (_state == CircuitState.Closed)
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
                    transition = ChangeState(CircuitState.Open);
                }
            }
        }

        Publish(transition);
    }

    /// <summary>Releases a half-open probe slot without recording an outcome (e.g. the probe was cancelled).</summary>
    public void AbandonProbe()
    {
        lock (_gate)
        {
            if (_state == CircuitState.HalfOpen)
            {
                _probeInFlight = false;
            }
        }
    }

    private void AbandonProbe(long probeGeneration)
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
        TransitionPublication? transition = null;

        lock (_gate)
        {
            if (_state != CircuitState.Isolated)
            {
                transition = ChangeState(CircuitState.Isolated);
            }
        }

        Publish(transition);
    }

    public void Reset()
    {
        TransitionPublication? transition = null;

        lock (_gate)
        {
            ResetMetrics();
            _probeInFlight = false;
            if (_state != CircuitState.Closed)
            {
                transition = ChangeState(CircuitState.Closed);
            }
        }

        Publish(transition);
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
            KevlarMetrics.RecordCircuitState(
                Volatile.Read(ref _metricsShieldName),
                stateChange.To);
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
