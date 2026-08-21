namespace Kevlar;

/// <summary>Describes the handled outcome that is about to open a circuit.</summary>
public readonly struct CircuitBreakerBreakDurationEvent
{
    private readonly KevlarContext? _context;

    internal CircuitBreakerBreakDurationEvent(
        Exception? exception,
        object? result,
        KevlarContext context)
    {
        Exception = exception;
        Result = result;
        _context = context;
    }

    /// <summary>The handled exception, or <see langword="null"/> when a result was handled.</summary>
    public Exception? Exception { get; }

    /// <summary>The handled result (boxed), or <see langword="null"/> when an exception occurred.</summary>
    public object? Result { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it or its property bag after
    /// the callback completes.
    /// </summary>
    public KevlarContext Context => _context
        ?? throw new InvalidOperationException("A default break-duration event has no execution context.");
}
