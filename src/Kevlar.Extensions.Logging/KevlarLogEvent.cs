namespace Kevlar.Extensions.Logging;

/// <summary>Describes one strategy event before its log level is selected.</summary>
public readonly struct KevlarLogEvent
{
    internal KevlarLogEvent(KevlarLogEventKind kind, in KevlarTelemetryEvent telemetryEvent)
    {
        Kind = kind;
        EventName = telemetryEvent.EventName;
        ShieldName = telemetryEvent.ShieldName;
        StrategyName = telemetryEvent.StrategyName;
        StrategyIndex = telemetryEvent.StrategyIndex;
        Attempt = telemetryEvent.AttemptNumber;
        Exception = telemetryEvent.Exception;
        Result = telemetryEvent.Result;
        Delay = telemetryEvent.Delay;
        Duration = telemetryEvent.Duration;
        FromState = telemetryEvent.FromState;
        ToState = telemetryEvent.ToState;
        RetryAfter = telemetryEvent.RetryAfter;
        CallbackKind = telemetryEvent.CallbackKind;
    }

    /// <summary>The mapped logging event kind.</summary>
    public KevlarLogEventKind Kind { get; }

    /// <summary>The stable telemetry event name.</summary>
    public string EventName { get; }

    /// <summary>The shield name, when configured.</summary>
    public string? ShieldName { get; }

    /// <summary>The strategy name.</summary>
    public string StrategyName { get; }

    /// <summary>The zero-based strategy position.</summary>
    public int StrategyIndex { get; }

    /// <summary>The zero-based attempt number.</summary>
    public int Attempt { get; }

    /// <summary>The associated exception, when present.</summary>
    public Exception? Exception { get; }

    /// <summary>The handled result, when available.</summary>
    public object? Result { get; }

    /// <summary>The computed retry, hedge, or break delay.</summary>
    public TimeSpan Delay { get; }

    /// <summary>The measured duration.</summary>
    public TimeSpan Duration { get; }

    /// <summary>The circuit state left by a transition.</summary>
    public CircuitState? FromState { get; }

    /// <summary>The circuit state entered by a transition.</summary>
    public CircuitState? ToState { get; }

    /// <summary>The estimated retry delay for a rejection.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>The callback family that failed.</summary>
    public CallbackErrorKind? CallbackKind { get; }
}
