namespace Kevlar;

/// <summary>Describes an exception thrown by an isolated callback or cleanup operation.</summary>
public readonly struct CallbackErrorEvent
{
    internal CallbackErrorEvent(
        CallbackErrorKind kind,
        string source,
        string? shieldName,
        int strategyIndex,
        int attemptNumber,
        Exception exception)
    {
        Kind = kind;
        Source = source;
        ShieldName = shieldName;
        StrategyIndex = strategyIndex;
        AttemptNumber = attemptNumber;
        Exception = exception;
    }

    /// <summary>Gets the callback or cleanup family that failed.</summary>
    public CallbackErrorKind Kind { get; }

    /// <summary>Gets the stable callback or integration identifier.</summary>
    public string Source { get; }

    /// <summary>Gets the shield name, or <see langword="null"/> for an unnamed shield.</summary>
    public string? ShieldName { get; }

    /// <summary>Gets the zero-based strategy position in the shield.</summary>
    public int StrategyIndex { get; }

    /// <summary>Gets the zero-based execution attempt number.</summary>
    public int AttemptNumber { get; }

    /// <summary>Gets the isolated exception.</summary>
    public Exception Exception { get; }
}
