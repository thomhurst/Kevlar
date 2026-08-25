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

    /// <summary>The active execution context. It is valid only during the listener callback.</summary>
    public KevlarContext Context => _context
        ?? throw new InvalidOperationException("The telemetry event is uninitialized.");
}
