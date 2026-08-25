namespace Kevlar;

/// <summary>Describes an exception being replaced by an untyped fallback.</summary>
public readonly struct FallbackEvent
{
    private readonly KevlarContext? _context;

    internal FallbackEvent(Exception exception, KevlarContext context)
    {
        Exception = exception;
        _context = context;
    }

    /// <summary>The handled exception being replaced.</summary>
    public Exception Exception { get; }

    /// <summary>
    /// The ambient execution context. It remains valid until the notification callback completes
    /// and must not be retained afterward.
    /// </summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);
}
