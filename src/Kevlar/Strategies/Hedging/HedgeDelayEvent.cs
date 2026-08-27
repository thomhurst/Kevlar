namespace Kevlar;

/// <summary>Describes an additional hedged attempt whose stagger delay is being selected.</summary>
public readonly struct HedgeDelayEvent
{
    private readonly KevlarContext? _context;

    internal HedgeDelayEvent(int attemptNumber, KevlarContext context, TimeSpan elapsed)
    {
        AttemptNumber = attemptNumber;
        _context = context;
        Elapsed = elapsed;
    }

    /// <summary>The zero-based execution attempt number (1 = first hedge after the initial attempt).</summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it after the generator completes.
    /// </summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);

    /// <summary>The elapsed time since the primary attempt started.</summary>
    public TimeSpan Elapsed { get; }
}
