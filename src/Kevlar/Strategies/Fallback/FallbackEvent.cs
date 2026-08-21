namespace Kevlar;

/// <summary>Describes an exception being replaced by an untyped fallback.</summary>
public readonly struct FallbackEvent
{
    internal FallbackEvent(Exception exception, KevlarContext context)
    {
        Exception = exception;
        Context = context;
    }

    /// <summary>The handled exception being replaced.</summary>
    public Exception Exception { get; }

    /// <summary>
    /// The ambient execution context. It remains valid until the notification callback completes
    /// and must not be retained afterward.
    /// </summary>
    public KevlarContext Context { get; }
}
