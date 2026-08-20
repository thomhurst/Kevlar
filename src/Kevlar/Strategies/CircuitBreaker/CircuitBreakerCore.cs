using Kevlar.Internal;

namespace Kevlar.Strategies;

/// <summary>
/// The thread-safe state machine behind a circuit breaker. Shared by the strategy
/// (which drives it from executions) and the monitor (manual control).
/// </summary>
internal sealed class CircuitBreakerCore
{
    private const int BucketCount = 10;

    private readonly Lock _gate = new();
    private readonly int? _consecutiveFailureLimit;
    private readonly double? _failureRatio;
    private readonly int _minimumThroughput;
    private readonly TimeSpan _samplingWindow;
    private readonly TimeSpan _bucketDuration;
    private readonly TimeSpan _breakDuration;
    private readonly Action<CircuitStateChangedEvent>? _onStateChanged;
    private readonly CircuitBreakerMonitor? _monitor;

    private readonly int[] _bucketFailures = new int[BucketCount];
    private readonly int[] _bucketSuccesses = new int[BucketCount];
    private long _currentBucket = -1;

    private CircuitState _state = CircuitState.Closed;
    private DateTimeOffset _openUntil;
    private int _consecutiveFailures;
    private bool _probeInFlight;
    private Exception? _lastException;

    public CircuitBreakerCore(CircuitBreakerOptions options)
    {
        Throw.IfOutOfRange(options.ConsecutiveFailures is <= 0, nameof(options), "ConsecutiveFailures must be positive.");
        Throw.IfOutOfRange(options.FailureRatio is <= 0 or > 1, nameof(options), "FailureRatio must be between 0 (exclusive) and 1 (inclusive).");
        Throw.IfOutOfRange(options.ConsecutiveFailures is not null && options.FailureRatio is not null, nameof(options), "Configure either ConsecutiveFailures or FailureRatio, not both.");
        Throw.IfOutOfRange(options.MinimumThroughput < 1, nameof(options), "MinimumThroughput must be at least 1.");
        Throw.IfOutOfRange(options.SamplingWindow <= TimeSpan.Zero, nameof(options), "SamplingWindow must be positive.");
        Throw.IfOutOfRange(options.BreakDuration <= TimeSpan.Zero, nameof(options), "BreakDuration must be positive.");

        _failureRatio = options.FailureRatio;
        _consecutiveFailureLimit = options.FailureRatio is null ? options.ConsecutiveFailures ?? 5 : null;
        _samplingWindow = options.SamplingWindow;
        _minimumThroughput = options.MinimumThroughput;
        _bucketDuration = TimeSpan.FromTicks(Math.Max(1, options.SamplingWindow.Ticks / BucketCount));
        _breakDuration = options.BreakDuration;
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

                case CircuitState.Open when timeProvider.GetUtcNow() >= _openUntil:
                    transition = ChangeState(CircuitState.HalfOpen);
                    _probeInFlight = true;
                    allowed = true;
                    break;

                case CircuitState.Open:
                    rejection = new CircuitOpenException(_openUntil - timeProvider.GetUtcNow(), isIsolated: false, _lastException);
                    allowed = false;
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
                _openUntil = timeProvider.GetUtcNow() + _breakDuration;
                transition = ChangeState(CircuitState.Open);
            }
            else if (_state == CircuitState.Closed && IsTripped(timeProvider))
            {
                _openUntil = timeProvider.GetUtcNow() + _breakDuration;
                transition = ChangeState(CircuitState.Open);
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
            if (_state != CircuitState.Closed)
            {
                ResetMetrics();
                _probeInFlight = false;
                transition = ChangeState(CircuitState.Closed);
            }
        }

        Publish(transition);
    }

    private bool IsTripped(TimeProvider timeProvider)
    {
        if (_consecutiveFailureLimit is { } limit)
        {
            return ++_consecutiveFailures >= limit;
        }

        var bucket = AdvanceBucket(timeProvider);
        _bucketFailures[bucket]++;

        int failures = 0, total = 0;
        for (var i = 0; i < BucketCount; i++)
        {
            failures += _bucketFailures[i];
            total += _bucketFailures[i] + _bucketSuccesses[i];
        }

        return total >= _minimumThroughput && (double)failures / total >= _failureRatio!.Value;
    }

    private int AdvanceBucket(TimeProvider timeProvider)
    {
        var absoluteBucket = timeProvider.GetUtcNow().UtcTicks / _bucketDuration.Ticks;

        if (_currentBucket == -1)
        {
            _currentBucket = absoluteBucket;
        }
        else if (absoluteBucket > _currentBucket)
        {
            var advance = Math.Min(absoluteBucket - _currentBucket, BucketCount);
            for (long i = 1; i <= advance; i++)
            {
                var index = (int)((_currentBucket + i) % BucketCount);
                _bucketFailures[index] = 0;
                _bucketSuccesses[index] = 0;
            }

            _currentBucket = absoluteBucket;
        }

        return (int)(_currentBucket % BucketCount);
    }

    private void ResetMetrics()
    {
        _consecutiveFailures = 0;
        _currentBucket = -1;
        Array.Clear(_bucketFailures, 0, BucketCount);
        Array.Clear(_bucketSuccesses, 0, BucketCount);
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
