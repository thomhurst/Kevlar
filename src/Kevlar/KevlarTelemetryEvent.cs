namespace Kevlar;

/// <summary>Describes one synchronous event emitted by a Kevlar strategy.</summary>
public readonly struct KevlarTelemetryEvent
{
    private readonly KevlarContext? _context;

    internal KevlarTelemetryEvent(
        string eventName,
        KevlarTelemetrySeverity severity,
        string? shieldName,
        string strategyName,
        int strategyIndex,
        int attemptNumber,
        bool isSuccess,
        Exception? exception,
        TimeSpan duration,
        string? operationKey,
        object? result,
        TimeSpan delay,
        CircuitState? fromState,
        CircuitState? toState,
        TimeSpan? retryAfter,
        string? rejectionKind,
        CallbackErrorKind? callbackKind,
        KevlarContext context)
    {
        EventName = eventName;
        Severity = severity;
        ShieldName = shieldName;
        StrategyName = strategyName;
        StrategyIndex = strategyIndex;
        AttemptNumber = attemptNumber;
        IsSuccess = isSuccess;
        Exception = exception;
        Duration = duration;
        OperationKey = operationKey;
        Result = result;
        Delay = delay;
        FromState = fromState;
        ToState = toState;
        RetryAfter = retryAfter;
        RejectionKind = rejectionKind;
        CallbackKind = callbackKind;
        _context = context;
    }

    /// <summary>The stable event name, such as <c>execution_attempt</c> or <c>retry</c>.</summary>
    public string EventName { get; }

    /// <summary>The event severity.</summary>
    public KevlarTelemetrySeverity Severity { get; }

    /// <summary>The executing shield name, if configured.</summary>
    public string? ShieldName { get; }

    /// <summary>The stable strategy name.</summary>
    public string StrategyName { get; }

    /// <summary>The zero-based strategy position, or <c>-1</c> for a caller-recorded event.</summary>
    public int StrategyIndex { get; }

    /// <summary>The zero-based execution-attempt number.</summary>
    public int AttemptNumber { get; }

    /// <summary>Whether the event's outcome succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>The associated exception, if any.</summary>
    public Exception? Exception { get; }

    /// <summary>The measured attempt duration, or <see cref="TimeSpan.Zero"/> when not applicable.</summary>
    public TimeSpan Duration { get; }

    /// <summary>The bounded logical operation key supplied through <see cref="KevlarKeys.OperationKey"/>.</summary>
    public string? OperationKey { get; }

    /// <summary>The handled result for result-aware events, when available.</summary>
    public object? Result { get; }

    /// <summary>The computed retry, hedge, or break delay, when applicable.</summary>
    public TimeSpan Delay { get; }

    /// <summary>The circuit state left by a transition, when applicable.</summary>
    public CircuitState? FromState { get; }

    /// <summary>The circuit state entered by a transition, when applicable.</summary>
    public CircuitState? ToState { get; }

    /// <summary>The estimated time until a rejected execution may be retried.</summary>
    public TimeSpan? RetryAfter { get; }

    internal string? RejectionKind { get; }

    /// <summary>The callback family that failed, when this is a callback-error event.</summary>
    public CallbackErrorKind? CallbackKind { get; }

    /// <summary>The active execution context. It is valid only during the listener callback.</summary>
    public KevlarContext Context => _context
        ?? throw new InvalidOperationException("The telemetry event is uninitialized.");

    internal KevlarTelemetryEvent WithResult(object? result) => new(
        EventName,
        Severity,
        ShieldName,
        StrategyName,
        StrategyIndex,
        AttemptNumber,
        IsSuccess,
        Exception,
        Duration,
        OperationKey,
        result,
        Delay,
        FromState,
        ToState,
        RetryAfter,
        RejectionKind,
        CallbackKind,
        Context);
}
