#if NET8_0_OR_GREATER
using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
#endif

namespace Kevlar.Testing;

/// <summary>
/// Records Kevlar metrics and strategy callbacks for deterministic tests. Assign the
/// <see cref="Record(RetryEvent)"/> overloads directly to strategy notification options.
/// </summary>
public sealed class TelemetryRecorder : IDisposable, IKevlarTelemetryListener
{
    private readonly object _gate = new();
    private readonly List<MetricRecord> _metrics = [];
    private readonly List<CallbackRecord> _callbacks = [];
    private readonly bool _captureCallbackErrors;
    private readonly List<TelemetryEventRecord> _events = [];
    private readonly IDisposable _telemetrySubscription;
    private TaskCompletionSource<bool> _changed = CreateSignal();
#if NET8_0_OR_GREATER
    private readonly MeterListener? _listener;
#endif
    private long _sequence;
    private bool _disposed;

    /// <summary>Creates a recorder with optional process-wide metric and callback-error capture.</summary>
    /// <param name="captureMetrics">Whether to subscribe to Kevlar metrics.</param>
    /// <param name="captureCallbackErrors">
    /// Whether to subscribe to process-wide callback-error diagnostics. Leave disabled when recording
    /// callbacks assigned directly to a shield to preserve per-pipeline isolation.
    /// </param>
    public TelemetryRecorder(
        bool captureMetrics = true,
        bool captureCallbackErrors = false)
    {
        _captureCallbackErrors = captureCallbackErrors;
        _telemetrySubscription = KevlarDiagnostics.Listen(this);
#if NET8_0_OR_GREATER
        if (captureMetrics)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = static (instrument, listener) =>
                {
                    if (instrument.Meter.Name == KevlarDiagnostics.MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<long>(
                (instrument, value, tags, _) => AddMetric(instrument.Name, value, tags));
            _listener.SetMeasurementEventCallback<double>(
                (instrument, value, tags, _) => AddMetric(instrument.Name, value, tags));
            _listener.Start();
        }
#else
        _ = captureMetrics;
#endif
        if (captureCallbackErrors)
        {
            KevlarDiagnostics.OnCallbackError += Record;
        }
    }

    /// <summary>Gets a stable snapshot of captured strategy telemetry events.</summary>
    public IReadOnlyList<TelemetryEventRecord> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    /// <summary>Gets a stable snapshot of captured metric measurements.</summary>
    public IReadOnlyList<MetricRecord> Metrics
    {
        get
        {
            lock (_gate)
            {
                return _metrics.ToArray();
            }
        }
    }

    /// <summary>Gets a stable snapshot of captured callback events.</summary>
    public IReadOnlyList<CallbackRecord> Callbacks
    {
        get
        {
            lock (_gate)
            {
                return _callbacks.ToArray();
            }
        }
    }

    /// <summary>Records an untyped retry callback.</summary>
    public void Record(RetryEvent item) => AddCallback(new CallbackRecord(
        0, CallbackKind.Retry, item.Context.ShieldName,
        strategyIndex: item.Context.StrategyIndex, retryNumber: item.RetryNumber,
        delay: item.Delay, exception: item.Exception, result: item.Result));

    /// <summary>Records a typed retry callback.</summary>
    public void Record<TResult>(RetryEvent<TResult> item) => AddCallback(new CallbackRecord(
        0, CallbackKind.Retry, item.Context.ShieldName,
        strategyIndex: item.Context.StrategyIndex, retryNumber: item.RetryNumber,
        delay: item.Delay, exception: item.Outcome.Exception, result: item.Outcome.Result));

    /// <summary>Records a timeout callback.</summary>
    public void Record(TimeoutEvent item) => AddCallback(new CallbackRecord(
        0, CallbackKind.Timeout, item.Context.ShieldName,
        strategyIndex: item.Context.StrategyIndex, timeout: item.Timeout));

    /// <summary>Records a hedge callback.</summary>
    public void Record(HedgeEvent item) => AddCallback(new CallbackRecord(
        0, CallbackKind.Hedge, item.Context.ShieldName,
        strategyIndex: item.Context.StrategyIndex, attemptNumber: item.AttemptNumber));

    /// <summary>Records an untyped fallback callback.</summary>
    public void Record(FallbackEvent item) => AddCallback(new CallbackRecord(
        0, CallbackKind.Fallback, item.Context.ShieldName,
        strategyIndex: item.Context.StrategyIndex, exception: item.Exception));

    /// <summary>Records a typed fallback callback.</summary>
    public void Record<TResult>(FallbackEvent<TResult> item) => AddCallback(new CallbackRecord(
        0, CallbackKind.Fallback, item.Context.ShieldName,
        strategyIndex: item.Context.StrategyIndex,
        exception: item.Outcome.Exception, result: item.Outcome.Result));

    /// <summary>Records a circuit-breaker transition callback.</summary>
    public void Record(CircuitBreakerStateChangedEvent item) => AddCallback(new CallbackRecord(
        0, CallbackKind.CircuitTransition, shieldName: item.Context.ShieldName,
        strategyIndex: item.Context.StrategyIndex,
        exception: item.LastException,
        from: item.From, to: item.To));

    /// <summary>Records a callback failure reported by Kevlar diagnostics.</summary>
    public void Record(CallbackErrorEvent item) => AddCallback(new CallbackRecord(
        0,
        CallbackKind.CallbackError,
        item.ShieldName,
        strategyIndex: item.StrategyIndex,
        exception: item.Exception,
        errorKind: item.Kind));

    /// <summary>Records an untyped circuit-breaker break-duration callback.</summary>
    public ValueTask<TimeSpan> Record(
        CircuitBreakerBreakDurationEvent item,
        TimeSpan breakDuration) =>
        RecordBreakDuration(
            item.Context,
            item.Exception,
            item.Result,
            item.FailureRate,
            item.FailureCount,
            item.ConsecutiveFailures,
            breakDuration);

    /// <summary>Records a typed circuit-breaker break-duration callback.</summary>
    public ValueTask<TimeSpan> Record<TResult>(
        CircuitBreakerBreakDurationEvent<TResult> item,
        TimeSpan breakDuration) =>
        RecordBreakDuration(
            item.Context,
            item.Outcome.Exception,
            item.Outcome.Result,
            item.FailureRate,
            item.FailureCount,
            item.ConsecutiveFailures,
            breakDuration);

    /// <summary>Waits until at least <paramref name="count"/> metrics have been captured.</summary>
    public Task WaitForMetricCountAsync(int count, CancellationToken cancellationToken = default) =>
        WaitForCountAsync(static recorder => recorder._metrics.Count, count, cancellationToken);

    /// <summary>Waits until at least <paramref name="count"/> callbacks have been captured.</summary>
    public Task WaitForCallbackCountAsync(int count, CancellationToken cancellationToken = default) =>
        WaitForCountAsync(static recorder => recorder._callbacks.Count, count, cancellationToken);

    /// <summary>Waits until at least <paramref name="count"/> telemetry events have been captured.</summary>
    public Task WaitForEventCountAsync(int count, CancellationToken cancellationToken = default) =>
        WaitForCountAsync(static recorder => recorder._events.Count, count, cancellationToken);

    /// <inheritdoc />
    void IKevlarTelemetryListener.OnEvent(in KevlarTelemetryEvent telemetryEvent)
    {
        TaskCompletionSource<bool> signal;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _events.Add(new TelemetryEventRecord(++_sequence, in telemetryEvent));
            signal = _changed;
            _changed = CreateSignal();
        }

        signal.TrySetResult(true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        TaskCompletionSource<bool> signal;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            signal = _changed;
        }

        if (_captureCallbackErrors)
        {
            KevlarDiagnostics.OnCallbackError -= Record;
        }

#if NET8_0_OR_GREATER
        _listener?.Dispose();
#endif
        _telemetrySubscription.Dispose();
        signal.TrySetResult(true);
    }

#if NET8_0_OR_GREATER
    private void AddMetric<T>(string instrumentName, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct, IConvertible
    {
        var copiedTags = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            copiedTags[tag.Key] = tag.Value;
        }

        AddMetric(new MetricRecord(
            0,
            instrumentName,
            Convert.ToDouble(value),
            new ReadOnlyDictionary<string, object?>(copiedTags)));
    }
#endif

    private void AddMetric(MetricRecord record)
    {
        TaskCompletionSource<bool> signal;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _metrics.Add(record.WithSequence(++_sequence));
            signal = _changed;
            _changed = CreateSignal();
        }

        signal.TrySetResult(true);
    }

    private void AddCallback(CallbackRecord record)
    {
        TaskCompletionSource<bool> signal;
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TelemetryRecorder));
            }

            _callbacks.Add(record.WithSequence(++_sequence));
            signal = _changed;
            _changed = CreateSignal();
        }

        signal.TrySetResult(true);
    }

    private ValueTask<TimeSpan> RecordBreakDuration(
        KevlarContext context,
        Exception? exception,
        object? result,
        double failureRate,
        long failureCount,
        int consecutiveFailures,
        TimeSpan breakDuration)
    {
        AddCallback(new CallbackRecord(
            0,
            CallbackKind.CircuitBreakDuration,
            context.ShieldName,
            strategyIndex: context.StrategyIndex,
            exception: exception,
            result: result,
            failureRate: failureRate,
            failureCount: failureCount,
            consecutiveFailures: consecutiveFailures));
        return new ValueTask<TimeSpan>(breakDuration);
    }

    private async Task WaitForCountAsync(
        Func<TelemetryRecorder, int> getCount,
        int count,
        CancellationToken cancellationToken)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        while (true)
        {
            Task signal;
            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(TelemetryRecorder));
                }

                if (getCount(this) >= count)
                {
                    return;
                }

                signal = _changed.Task;
            }

            await WaitWithCancellationAsync(signal, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            await task.ConfigureAwait(false);
            return;
        }

        var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(), cancellation);
        var completed = await Task.WhenAny(task, cancellation.Task).ConfigureAwait(false);
        if (ReferenceEquals(completed, cancellation.Task))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        await task.ConfigureAwait(false);
    }

    private static TaskCompletionSource<bool> CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
