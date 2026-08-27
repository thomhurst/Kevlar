namespace Kevlar.Testing;

/// <summary>An immutable snapshot of one Kevlar telemetry event.</summary>
public sealed class TelemetryEventRecord
{
    internal TelemetryEventRecord(long sequence, in KevlarTelemetryEvent telemetryEvent)
    {
        Sequence = sequence;
        EventName = telemetryEvent.EventName;
        Severity = telemetryEvent.Severity;
        ShieldName = telemetryEvent.ShieldName;
        StrategyName = telemetryEvent.StrategyName;
        StrategyIndex = telemetryEvent.StrategyIndex;
        AttemptNumber = telemetryEvent.AttemptNumber;
        IsSuccess = telemetryEvent.IsSuccess;
        IsWinner = telemetryEvent.IsWinner;
        IsCancelled = telemetryEvent.IsCancelled;
        Exception = telemetryEvent.Exception;
        Duration = telemetryEvent.Duration;
        OperationKey = telemetryEvent.OperationKey;
    }

    /// <summary>The recorder-wide sequence number.</summary>
    public long Sequence { get; }

    /// <inheritdoc cref="KevlarTelemetryEvent.EventName"/>
    public string EventName { get; }

    /// <inheritdoc cref="KevlarTelemetryEvent.Severity"/>
    public KevlarTelemetrySeverity Severity { get; }

    /// <inheritdoc cref="KevlarTelemetryEvent.ShieldName"/>
    public string? ShieldName { get; }

    /// <inheritdoc cref="KevlarTelemetryEvent.StrategyName"/>
    public string StrategyName { get; }

    /// <inheritdoc cref="KevlarTelemetryEvent.StrategyIndex"/>
    public int StrategyIndex { get; }

    /// <inheritdoc cref="KevlarTelemetryEvent.AttemptNumber"/>
    public int AttemptNumber { get; }

    /// <inheritdoc cref="KevlarTelemetryEvent.IsSuccess"/>
    public bool IsSuccess { get; }

    /// <inheritdoc cref="KevlarTelemetryEvent.IsWinner"/>
    public bool IsWinner { get; }

    /// <inheritdoc cref="KevlarTelemetryEvent.IsCancelled"/>
    public bool IsCancelled { get; }

    /// <inheritdoc cref="KevlarTelemetryEvent.Exception"/>
    public Exception? Exception { get; }

    /// <inheritdoc cref="KevlarTelemetryEvent.Duration"/>
    public TimeSpan Duration { get; }

    /// <inheritdoc cref="KevlarTelemetryEvent.OperationKey"/>
    public string? OperationKey { get; }
}
