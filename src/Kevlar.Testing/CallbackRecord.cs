namespace Kevlar.Testing;

/// <summary>An immutable snapshot of a strategy callback.</summary>
public sealed class CallbackRecord
{
    internal CallbackRecord(
        long sequence,
        CallbackKind kind,
        string? shieldName = null,
        int? strategyIndex = null,
        int? retryNumber = null,
        int? attemptNumber = null,
        TimeSpan? delay = null,
        TimeSpan? timeout = null,
        Exception? exception = null,
        object? result = null,
        CircuitState? from = null,
        CircuitState? to = null,
        double? failureRate = null,
        long? failureCount = null,
        int? consecutiveFailures = null,
        CallbackErrorKind? errorKind = null)
    {
        Sequence = sequence;
        Kind = kind;
        ShieldName = shieldName;
        StrategyIndex = strategyIndex;
        RetryNumber = retryNumber;
        AttemptNumber = attemptNumber;
        Delay = delay;
        Timeout = timeout;
        Exception = exception;
        Result = result;
        From = from;
        To = to;
        FailureRate = failureRate;
        FailureCount = failureCount;
        ConsecutiveFailures = consecutiveFailures;
        ErrorKind = errorKind;
    }

    /// <summary>The recorder-wide sequence number.</summary>
    public long Sequence { get; }

    /// <summary>The callback family.</summary>
    public CallbackKind Kind { get; }

    /// <summary>The shield name copied from the callback context, when available.</summary>
    public string? ShieldName { get; }

    /// <summary>The zero-based strategy position copied from the callback context.</summary>
    public int? StrategyIndex { get; }

    /// <summary>The 1-based retry number, when this is a retry callback.</summary>
    public int? RetryNumber { get; }

    /// <summary>The 1-based execution number, when this is a hedge callback.</summary>
    public int? AttemptNumber { get; }

    /// <summary>The retry delay, when applicable.</summary>
    public TimeSpan? Delay { get; }

    /// <summary>The exceeded timeout, when applicable.</summary>
    public TimeSpan? Timeout { get; }

    /// <summary>The captured exception, when applicable.</summary>
    public Exception? Exception { get; }

    /// <summary>The captured result, when applicable.</summary>
    public object? Result { get; }

    /// <summary>The circuit state left, when applicable.</summary>
    public CircuitState? From { get; }

    /// <summary>The circuit state entered, when applicable.</summary>
    public CircuitState? To { get; }

    /// <summary>The circuit failure ratio, when this is a break-duration callback.</summary>
    public double? FailureRate { get; }

    /// <summary>The circuit failure count, when this is a break-duration callback.</summary>
    public long? FailureCount { get; }

    /// <summary>The consecutive circuit failure count, when this is a break-duration callback.</summary>
    public int? ConsecutiveFailures { get; }

    /// <summary>The failed callback family, when this is a callback-error record.</summary>
    public CallbackErrorKind? ErrorKind { get; }

    internal CallbackRecord WithSequence(long sequence) => new(
        sequence,
        Kind,
        ShieldName,
        strategyIndex: StrategyIndex,
        retryNumber: RetryNumber,
        attemptNumber: AttemptNumber,
        delay: Delay,
        timeout: Timeout,
        exception: Exception,
        result: Result,
        from: From,
        to: To,
        failureRate: FailureRate,
        failureCount: FailureCount,
        consecutiveFailures: ConsecutiveFailures,
        errorKind: ErrorKind);
}
