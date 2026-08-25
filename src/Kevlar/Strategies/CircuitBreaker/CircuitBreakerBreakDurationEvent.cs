namespace Kevlar;

/// <summary>Describes the handled outcome that is about to open a circuit.</summary>
public readonly struct CircuitBreakerBreakDurationEvent
{
    private readonly KevlarContext? _context;

    internal CircuitBreakerBreakDurationEvent(
        Exception? exception,
        object? result,
        double failureRate,
        long failureCount,
        int consecutiveFailures,
        KevlarContext context)
    {
        Exception = exception;
        Result = result;
        FailureRate = failureRate;
        FailureCount = failureCount;
        ConsecutiveFailures = consecutiveFailures;
        _context = context;
    }

    /// <summary>The handled exception, or <see langword="null"/> when a result was handled.</summary>
    public Exception? Exception { get; }

    /// <summary>The handled result (boxed), or <see langword="null"/> when an exception occurred.</summary>
    public object? Result { get; }

    /// <summary>The handled-failure ratio when the circuit opened.</summary>
    public double FailureRate { get; }

    /// <summary>The handled-failure count when the circuit opened.</summary>
    public long FailureCount { get; }

    /// <summary>The consecutive handled-failure count when the circuit opened.</summary>
    public int ConsecutiveFailures { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it or its property bag after
    /// the callback completes.
    /// </summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);
}

/// <summary>
/// Describes the typed handled outcome that is about to open a circuit.
/// </summary>
/// <typeparam name="TResult">The shield result type.</typeparam>
public readonly struct CircuitBreakerBreakDurationEvent<TResult>
{
    private readonly KevlarContext? _context;

    internal CircuitBreakerBreakDurationEvent(
        Outcome<TResult> outcome,
        double failureRate,
        long failureCount,
        int consecutiveFailures,
        KevlarContext context)
    {
        Outcome = outcome;
        FailureRate = failureRate;
        FailureCount = failureCount;
        ConsecutiveFailures = consecutiveFailures;
        _context = context;
    }

    /// <summary>The handled outcome that opened the circuit.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>The handled-failure ratio when the circuit opened.</summary>
    public double FailureRate { get; }

    /// <summary>The handled-failure count when the circuit opened.</summary>
    public long FailureCount { get; }

    /// <summary>The consecutive handled-failure count when the circuit opened.</summary>
    public int ConsecutiveFailures { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it or its property bag after
    /// the callback completes.
    /// </summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);
}
