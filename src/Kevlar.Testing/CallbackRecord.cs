namespace Kevlar.Testing;

/// <summary>An immutable snapshot of a strategy callback.</summary>
public sealed class CallbackRecord
{
    internal CallbackRecord(
        long sequence,
        CallbackKind kind,
        string? shieldName = null,
        int? attempt = null,
        TimeSpan? delay = null,
        TimeSpan? timeout = null,
        Exception? exception = null,
        object? result = null,
        CircuitState? from = null,
        CircuitState? to = null)
    {
        Sequence = sequence;
        Kind = kind;
        ShieldName = shieldName;
        Attempt = attempt;
        Delay = delay;
        Timeout = timeout;
        Exception = exception;
        Result = result;
        From = from;
        To = to;
    }

    /// <summary>The recorder-wide sequence number.</summary>
    public long Sequence { get; }

    /// <summary>The callback family.</summary>
    public CallbackKind Kind { get; }

    /// <summary>The shield name copied from the callback context, when available.</summary>
    public string? ShieldName { get; }

    /// <summary>The retry or hedge attempt number, when applicable.</summary>
    public int? Attempt { get; }

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

    internal CallbackRecord WithSequence(long sequence) => new(
        sequence, Kind, ShieldName, Attempt, Delay, Timeout, Exception, Result, From, To);
}
