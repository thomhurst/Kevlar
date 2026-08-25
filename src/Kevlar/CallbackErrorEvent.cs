namespace Kevlar;

/// <summary>Describes an exception thrown by a strategy notification or observer.</summary>
public readonly struct CallbackErrorEvent
{
    internal CallbackErrorEvent(
        CallbackErrorKind kind,
        string? shieldName,
        int strategyIndex,
        Exception exception)
    {
        Kind = kind;
        ShieldName = shieldName;
        StrategyIndex = strategyIndex;
        Exception = exception;
    }

    /// <summary>Gets the callback family that failed.</summary>
    public CallbackErrorKind Kind { get; }

    /// <summary>Gets the shield name, or <see langword="null"/> for an unnamed shield.</summary>
    public string? ShieldName { get; }

    /// <summary>Gets the zero-based strategy position in the shield.</summary>
    public int StrategyIndex { get; }

    /// <summary>Gets the exception thrown by the callback.</summary>
    public Exception Exception { get; }
}
