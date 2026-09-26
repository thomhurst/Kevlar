using System.Diagnostics;

namespace Kevlar.Extensions.Tracing;

/// <summary>Adds Kevlar telemetry events to existing, sampled application activities.</summary>
public static class KevlarTracing
{
    /// <summary>Subscribes globally to Kevlar telemetry and enriches the activity current during each callback.</summary>
    /// <param name="options">Options copied at registration time, or null to use the defaults.</param>
    /// <returns>An idempotently disposable subscription. An in-flight callback may finish after disposal.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The maximum string tag length is not positive.</exception>
    /// <remarks>
    /// Keep one subscription for the application lifetime. Multiple subscriptions independently add events,
    /// including duplicates. No activities are created, stopped, retained, or assigned an error status.
    /// Absent, stopped, propagation-only, or unrecorded activities are ignored. Async execution uses the
    /// normal execution-context flow of Activity.Current; suppressed flow cannot be reconstructed.
    /// Contexts, property bags, results, and exception objects are never retained in activity events.
    /// Listener failures cannot change the protected operation's outcome.
    /// </remarks>
    public static IDisposable Listen(KevlarTracingOptions? options = null)
    {
        options ??= new KevlarTracingOptions();
        var maximumLength = options.MaximumTagValueLength;
        if (maximumLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumTagValueLength must be positive.");
        }

        var listener = new TracingListener(
            options.IncludeExceptionDetails, options.IncludeOperationKey, maximumLength);
        listener.Subscribe();
        return listener;
    }

    private sealed class TracingListener(bool includeExceptionDetails, bool includeOperationKey, int maximumLength)
        : IKevlarTelemetryListener, IKevlarResultTelemetryListener, IDisposable
    {
        private IDisposable? _subscription;
        private int _disposed;

        internal void Subscribe() => _subscription = KevlarDiagnostics.Listen(this);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _subscription?.Dispose();
            }
        }

        // Results are deliberately ignored without forcing value-type results to be boxed by the dispatcher.
        void IKevlarResultTelemetryListener.OnResultEvent<T>(in KevlarTelemetryEvent telemetryEvent, in T result)
            => OnEvent(in telemetryEvent);

        public void OnEvent(in KevlarTelemetryEvent telemetryEvent)
        {
            var activity = Activity.Current;
            if (Volatile.Read(ref _disposed) != 0 || activity is null || !activity.IsAllDataRequested
                || !activity.Recorded || activity.Duration != TimeSpan.Zero)
            {
                return;
            }

            var tags = new ActivityTagsCollection
            {
                { "kevlar.event.name", Limit(telemetryEvent.EventName) },
                { "kevlar.strategy.name", Limit(telemetryEvent.StrategyName) },
                { "kevlar.strategy.index", telemetryEvent.StrategyIndex },
                { "kevlar.attempt.number", telemetryEvent.AttemptNumber },
                { "kevlar.outcome.success", telemetryEvent.IsSuccess },
                { "kevlar.duration.seconds", telemetryEvent.Duration.TotalSeconds },
                { "kevlar.delay.seconds", telemetryEvent.Delay.TotalSeconds },
            };
            Add(tags, "kevlar.shield.name", telemetryEvent.ShieldName);
            Add(tags, "kevlar.rejection.kind", telemetryEvent.RejectionKind);
            Add(tags, "kevlar.suppression.reason", telemetryEvent.SuppressionReason);
            Add(tags, "kevlar.callback.source", telemetryEvent.CallbackSource);
            if (telemetryEvent.CallbackKind is { } callbackKind)
            {
                Add(tags, "kevlar.callback.kind", callbackKind.ToString());
            }
            if (telemetryEvent.FromState is { } fromState)
            {
                Add(tags, "kevlar.circuit.from", fromState.ToString());
            }
            if (telemetryEvent.ToState is { } toState)
            {
                Add(tags, "kevlar.circuit.to", toState.ToString());
            }
            if (telemetryEvent.RetryAfter is { } retryAfter)
            {
                tags.Add("kevlar.retry_after.seconds", retryAfter.TotalSeconds);
            }
            if (telemetryEvent.EventName == "hedge_attempt")
            {
                tags.Add("kevlar.hedge.winner", telemetryEvent.IsWinner);
                tags.Add("kevlar.hedge.cancelled", telemetryEvent.IsCancelled);
            }
            if (includeOperationKey)
            {
                Add(tags, "kevlar.operation.key", telemetryEvent.OperationKey);
            }
            if (telemetryEvent.Exception is { } exception)
            {
                Add(tags, "exception.type", exception.GetType().FullName);
                if (includeExceptionDetails)
                {
                    Add(tags, "exception.message", exception.Message);
                    Add(tags, "exception.stacktrace", exception.StackTrace);
                }
            }

            // A fixed event name also bounds the schema for application-defined telemetry events.
            activity.AddEvent(new ActivityEvent("kevlar.strategy", tags: tags));
        }

        private void Add(ActivityTagsCollection tags, string name, string? value)
        {
            if (value is not null)
            {
                tags.Add(name, Limit(value));
            }
        }

        private string Limit(string value)
        {
            if (value.Length <= maximumLength)
            {
                return value;
            }

            var length = maximumLength;
            if (char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
            {
                length--;
            }
            return value.Substring(0, length);
        }
    }
}
