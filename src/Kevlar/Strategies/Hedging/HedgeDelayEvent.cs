namespace Kevlar;

/// <summary>Describes an additional hedged attempt whose stagger delay is being selected.</summary>
public readonly struct HedgeDelayEvent
{
    internal HedgeDelayEvent(int attemptNumber, KevlarContext context, TimeSpan elapsed)
    {
        AttemptNumber = attemptNumber;
        Context = context;
        Elapsed = elapsed;
    }

    /// <summary>The 1-based number of the attempt that would be launched (2 = first hedge).</summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it after the generator completes.
    /// </summary>
    public KevlarContext Context { get; }

    /// <summary>The elapsed time since the primary attempt started.</summary>
    public TimeSpan Elapsed { get; }
}
