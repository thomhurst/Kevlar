namespace Kevlar;

/// <summary>Describes an outcome being replaced by a fallback.</summary>
public readonly struct FallbackEvent<TResult>
{
    internal FallbackEvent(Outcome<TResult> outcome, KevlarContext context)
    {
        Outcome = outcome;
        Context = context;
    }

    /// <summary>The handled outcome — the exception or result value being replaced.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>
    /// The ambient execution context. It remains valid until the notification callback completes
    /// and must not be retained afterward.
    /// </summary>
    public KevlarContext Context { get; }
}
