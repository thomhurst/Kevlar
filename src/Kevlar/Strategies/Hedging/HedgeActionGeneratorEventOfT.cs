namespace Kevlar;

/// <summary>Arguments used to select a result-returning operation for a hedged attempt.</summary>
public readonly struct HedgeActionGeneratorEvent<TResult>
{
    internal HedgeActionGeneratorEvent(
        int attempt,
        KevlarContext context,
        Func<CancellationToken, ValueTask<TResult>> originalAction)
    {
        Attempt = attempt;
        Context = context;
        OriginalAction = originalAction;
    }

    /// <summary>The 1-based attempt number (2 = first hedge).</summary>
    public int Attempt { get; }

    /// <summary>The isolated context that belongs to this attempt.</summary>
    public KevlarContext Context { get; }

    /// <summary>
    /// The original operation, including strategies nested inside the hedge. The supplied token
    /// becomes the cancellation token for that nested execution.
    /// </summary>
    public Func<CancellationToken, ValueTask<TResult>> OriginalAction { get; }
}
