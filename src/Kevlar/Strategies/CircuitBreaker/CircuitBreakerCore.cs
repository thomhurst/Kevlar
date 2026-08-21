using System.Diagnostics;
using System.Runtime.CompilerServices;
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

    private readonly long[] _bucketFailures = new long[BucketCount];
    private readonly long[] _bucketSuccesses = new long[BucketCount];
    private double _currentBucketStart = double.NaN;
    private int _currentBucketIndex;

    private CircuitState _state = CircuitState.Closed;
    private double _latestTimestamp;
    private double _openUntilTimestamp;
    private int _consecutiveFailures;
    private bool _probeInFlight;
    private Exception? _lastException;

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

    /// <summary>
    /// Gates an execution. Returns <see langword="false"/> with a rejection when the circuit
    /// refuses it; a <see langword="true"/> return during half-open marks a probe in flight,
    /// so the caller must report back via Record* or <see cref="AbandonProbe"/>.
    /// </summary>
    public bool TryEnter(TimeProvider timeProvider, out CircuitOpenException? rejection)
    {
        CircuitStateChangedEvent? transition = null;
        bool allowed;
        rejection = null;

        lock (_gate)
        {
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
                        allowed = true;
                    }

                    break;
            }
        }

        Publish(transition);
        return allowed;
    }

    public void RecordSuccess(TimeProvider timeProvider)
    {
        CircuitStateChangedEvent? transition = null;

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
        CircuitStateChangedEvent? transition = null;

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

    public void Isolate()
    {
        CircuitStateChangedEvent? transition = null;

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
        CircuitStateChangedEvent? transition = null;

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

    private CircuitStateChangedEvent ChangeState(CircuitState next)
    {
        var transition = new CircuitStateChangedEvent(_state, next, _lastException);
        _state = next;
        return transition;
    }

    private void Publish(CircuitStateChangedEvent? transition)
    {
        if (transition is { } stateChange)
        {
            KevlarMetrics.CircuitTransition(stateChange.From, stateChange.To);
            _onStateChanged?.Invoke(stateChange);
            _monitor?.Raise(in stateChange);
        }
    }
}
