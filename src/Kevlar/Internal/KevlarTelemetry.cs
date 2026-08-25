namespace Kevlar.Internal;

internal static class KevlarTelemetry
{
    private static readonly object Sync = new();
    private static IKevlarTelemetryListener[] _listeners = [];

    public static bool EventEnabled =>
        Volatile.Read(ref _listeners).Length != 0
        || KevlarMetrics.StrategyEventsEnabled;

    public static bool AttemptEnabled => EventEnabled || KevlarMetrics.AttemptDurationEnabled;

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
        bool recordAttemptDuration = false)
    {
        var listeners = Volatile.Read(ref _listeners);
        if (listeners.Length == 0
            && !KevlarMetrics.StrategyEventsEnabled
            && (!recordAttemptDuration || !KevlarMetrics.AttemptDurationEnabled))
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
            context);

        KevlarMetrics.StrategyEvent(in telemetryEvent, recordAttemptDuration);
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
    }

    private static void Unsubscribe(IKevlarTelemetryListener listener)
    {
        lock (Sync)
        {
            var current = _listeners;
            var index = Array.IndexOf(current, listener);
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
