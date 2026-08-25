namespace Kevlar;

/// <summary>Arguments used to select a result-returning operation for a hedged attempt.</summary>
public readonly struct HedgeActionGeneratorEvent<TResult>
{
    internal HedgeActionGeneratorEvent(
        int attemptNumber,
        KevlarContext context,
        Func<CancellationToken, ValueTask<TResult>> originalAction,
        Outcome<TResult>? outcome)
    {
        AttemptNumber = attemptNumber;
        Context = context;
        OriginalAction = originalAction;
        Outcome = outcome;
    }

    /// <summary>The 1-based execution number (2 = first hedge).</summary>
    public int AttemptNumber { get; }

    /// <summary>The isolated context that belongs to this attempt.</summary>
    public KevlarContext Context { get; }

    /// <summary>
    /// The original operation, including strategies nested inside the hedge. The supplied token
    /// becomes the cancellation token for that nested execution. Invoke it before the generated
    /// action completes; invoking a retained delegate afterward throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public Func<CancellationToken, ValueTask<TResult>> OriginalAction { get; }

    /// <summary>
    /// The latest handled outcome available before this attempt was launched, or
    /// <see langword="null"/> when pending attempts have not produced an outcome yet.
    /// </summary>
    public Outcome<TResult>? Outcome { get; }
}
