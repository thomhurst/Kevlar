namespace Kevlar;

/// <summary>Describes an outcome being replaced by a fallback.</summary>
public readonly struct FallbackEvent
{
    internal FallbackEvent(Exception? exception, object? result, KevlarContext context)
    {
        Exception = exception;
        Result = result;
        Context = context;
    }

    /// <summary>The exception being handled, or <see langword="null"/> when a result was handled.</summary>
    public Exception? Exception { get; }

    /// <summary>The handled result (boxed), or <see langword="null"/> when an exception occurred.</summary>
    public object? Result { get; }

    /// <summary>The ambient execution context.</summary>
    public KevlarContext Context { get; }
}
