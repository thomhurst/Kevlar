namespace Kevlar;

/// <summary>Arguments used to select a void-returning operation for a hedged attempt.</summary>
public readonly struct HedgeActionGeneratorEvent
{
    private readonly KevlarContext? _context;

    internal HedgeActionGeneratorEvent(
        int attemptNumber,
        KevlarContext context,
        Func<CancellationToken, ValueTask> originalAction)
    {
        AttemptNumber = attemptNumber;
        _context = context;
        OriginalAction = originalAction;
    }

    /// <summary>The zero-based execution attempt number (1 = first hedge after the initial attempt).</summary>
    public int AttemptNumber { get; }

    /// <summary>The isolated context that belongs to this attempt.</summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);

    /// <summary>
    /// The original operation, including strategies nested inside the hedge. The supplied token
    /// becomes the cancellation token for that nested execution. Invoke it before the generated
    /// action completes; invoking a retained delegate afterward throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public Func<CancellationToken, ValueTask> OriginalAction { get; }
}
