namespace Kevlar.Internal;

internal static class KevlarEventSource
{
    private static readonly object Gate = new();
    private static Entry[] _entries = [];

    public static bool Enabled => Volatile.Read(ref _entries).Length != 0;

    public static IDisposable Subscribe(KevlarEventListener listener)
    {
        var entry = new Entry(listener);
        lock (Gate)
        {
            var current = _entries;
            var updated = new Entry[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[current.Length] = entry;
            Volatile.Write(ref _entries, updated);
        }

        return new Subscription(entry);
    }

    public static void ExecutionStarted<T>(KevlarContext context)
    {
        var telemetryEvent = new KevlarEvent<T>(
            KevlarEventKind.ExecutionStarted,
            KevlarEventSeverity.Debug,
            KevlarStrategyKind.None,
            strategyIndex: -1,
            attempt: 0,
            duration: TimeSpan.Zero,
            handled: false,
            outcome: default,
            hasOutcome: false,
            context);
        Publish(in telemetryEvent);
    }

    public static void ExecutionCompleted<T>(
        KevlarContext context,
        in Outcome<T> outcome,
        TimeSpan duration)
    {
        var telemetryEvent = new KevlarEvent<T>(
            KevlarEventKind.ExecutionCompleted,
            outcome.IsSuccess ? KevlarEventSeverity.Information : KevlarEventSeverity.Error,
            KevlarStrategyKind.None,
            strategyIndex: -1,
            attempt: 0,
            duration,
            handled: false,
            outcome,
            hasOutcome: true,
            context);
        Publish(in telemetryEvent);
    }

    private static void Publish<T>(in KevlarEvent<T> telemetryEvent)
    {
        var snapshot = Volatile.Read(ref _entries);
        foreach (var entry in snapshot)
        {
            try
            {
                if (entry.Listener.IsEnabled(telemetryEvent.Kind))
                {
                    entry.Listener.OnEvent(in telemetryEvent);
                }
            }
            catch
            {
                // Telemetry must never alter pipeline behavior or prevent later listeners.
            }
        }
    }

    private static void Unsubscribe(Entry entry)
    {
        lock (Gate)
        {
            var current = _entries;
            var index = Array.IndexOf(current, entry);
            if (index < 0)
            {
                return;
            }

            if (current.Length == 1)
            {
                Volatile.Write(ref _entries, []);
                return;
            }

            var updated = new Entry[current.Length - 1];
            Array.Copy(current, 0, updated, 0, index);
            Array.Copy(current, index + 1, updated, index, current.Length - index - 1);
            Volatile.Write(ref _entries, updated);
        }
    }

    private sealed class Entry(KevlarEventListener listener)
    {
        public KevlarEventListener Listener { get; } = listener;
    }

    private sealed class Subscription(Entry entry) : IDisposable
    {
        private Entry? _entry = entry;

        public void Dispose()
        {
            var removed = Interlocked.Exchange(ref _entry, null);
            if (removed is not null)
            {
                Unsubscribe(removed);
            }
        }
    }
}
