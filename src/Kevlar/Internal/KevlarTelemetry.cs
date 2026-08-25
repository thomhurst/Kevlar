namespace Kevlar.Internal;

internal static class KevlarTelemetry
{
    private static readonly object Sync = new();
    private static IKevlarTelemetryListener[] _listeners = [];

    public static bool EventEnabled =>
        Volatile.Read(ref _listeners).Length != 0
        || KevlarMetrics.StrategyEventsEnabled;

    public static bool AttemptEnabled => EventEnabled || KevlarMetrics.AttemptDurationEnabled;

    public static bool IsEventEnabled(KevlarContext context) =>
        EventEnabled || context.TelemetryListener is not null;

    public static bool ShouldCaptureResult(KevlarContext context)
    {
        foreach (var listener in Volatile.Read(ref _listeners))
        {
            if (listener is not IKevlarResultTelemetryListener resultListener
                || resultListener.ShouldCaptureResult)
            {
                return true;
            }
        }

        return context.TelemetryListener is { } contextListener
            && (contextListener is not IKevlarResultTelemetryListener contextResultListener
                || contextResultListener.ShouldCaptureResult);
    }

    public static IDisposable Subscribe(IKevlarTelemetryListener listener)
    {
        lock (Sync)
        {
            var current = _listeners;
            var updated = new IKevlarTelemetryListener[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[^1] = listener;
            Volatile.Write(ref _listeners, updated);
        }

        return new Subscription(listener);
    }

    public static void Record(
        KevlarContext context,
        string strategyName,
        string eventName,
        KevlarTelemetrySeverity severity,
        int strategyIndex,
        int attemptNumber,
        bool isSuccess,
        Exception? exception = null,
        TimeSpan duration = default,
        bool recordAttemptDuration = false,
        object? result = null,
        TimeSpan delay = default,
        CircuitState? fromState = null,
        CircuitState? toState = null,
        TimeSpan? retryAfter = null,
        string? rejectionKind = null,
        CallbackErrorKind? callbackKind = null,
        bool localOnly = false)
    {
        var listeners = localOnly ? [] : Volatile.Read(ref _listeners);
        var contextListener = context.TelemetryListener;
        if (listeners.Length == 0
            && contextListener is null
            && (localOnly
                || (!KevlarMetrics.StrategyEventsEnabled
                    && (!recordAttemptDuration || !KevlarMetrics.AttemptDurationEnabled))))
        {
            return;
        }

        _ = context.Properties.TryGet(KevlarKeys.OperationKey, out string? operationKey);
        var telemetryEvent = new KevlarTelemetryEvent(
            eventName,
            severity,
            context.ShieldName,
            strategyName,
            strategyIndex,
            attemptNumber,
            isSuccess,
            exception,
            duration,
            operationKey,
            result,
            delay,
            fromState,
            toState,
            retryAfter,
            rejectionKind,
            callbackKind,
            context);

        if (!localOnly)
        {
            KevlarMetrics.StrategyEvent(in telemetryEvent, recordAttemptDuration);
        }
        foreach (var listener in listeners)
        {
            try
            {
                listener.OnEvent(in telemetryEvent);
            }
            catch
            {
                // Telemetry listeners must not change execution behavior.
            }
        }

        if (contextListener is not null)
        {
            try
            {
                contextListener.OnEvent(in telemetryEvent);
            }
            catch
            {
                // Telemetry listeners must not change execution behavior.
            }
        }
    }

    private static void Unsubscribe(IKevlarTelemetryListener listener)
    {
        lock (Sync)
        {
            var current = _listeners;
            var index = -1;
            for (var candidate = 0; candidate < current.Length; candidate++)
            {
                if (ReferenceEquals(current[candidate], listener))
                {
                    index = candidate;
                    break;
                }
            }
            if (index < 0)
            {
                return;
            }

            if (current.Length == 1)
            {
                Volatile.Write(ref _listeners, []);
                return;
            }

            var updated = new IKevlarTelemetryListener[current.Length - 1];
            Array.Copy(current, 0, updated, 0, index);
            Array.Copy(current, index + 1, updated, index, current.Length - index - 1);
            Volatile.Write(ref _listeners, updated);
        }
    }

    private sealed class Subscription(IKevlarTelemetryListener listener) : IDisposable
    {
        private IKevlarTelemetryListener? _listener = listener;

        public void Dispose()
        {
            var removed = Interlocked.Exchange(ref _listener, null);
            if (removed is not null)
            {
                Unsubscribe(removed);
            }
        }
    }
}
