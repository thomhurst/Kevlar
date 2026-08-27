namespace Kevlar;

/// <summary>
/// Describes a hedged attempt being launched, with the latest handled outcome typed as
/// <typeparamref name="TResult"/>.
/// </summary>
public readonly struct HedgeEvent<TResult>
{
    private readonly KevlarContext? _context;

    internal HedgeEvent(
        int attemptNumber,
        Outcome<TResult>? outcome,
        KevlarContext context)
    {
        AttemptNumber = attemptNumber;
        Outcome = outcome;
        _context = context;
    }

    /// <summary>The zero-based execution attempt number (1 = first hedge after the initial attempt).</summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// The latest handled outcome available before this attempt was launched, or
    /// <see langword="null"/> when pending attempts have not produced an outcome yet.
    /// </summary>
    public Outcome<TResult>? Outcome { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it after the hedge callback's
    /// returned task completes.
    /// </summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);
}
