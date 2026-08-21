namespace Kevlar;

/// <summary>
/// A structured lifecycle or strategy event. The event and its <see cref="Context"/> are valid
/// only for the duration of <see cref="KevlarEventListener.OnEvent{T}"/> and must not be retained.
/// </summary>
/// <typeparam name="T">The execution result type, preserved without boxing.</typeparam>
public readonly struct KevlarEvent<T>
{
    private readonly KevlarContext? _context;
    private readonly Outcome<T> _outcome;

    internal KevlarEvent(
        KevlarEventKind kind,
        KevlarEventSeverity severity,
        KevlarStrategyKind strategyKind,
        int strategyIndex,
        int attempt,
        TimeSpan duration,
        bool handled,
        Outcome<T> outcome,
        bool hasOutcome,
        KevlarContext context)
    {
        Kind = kind;
        Severity = severity;
        StrategyKind = strategyKind;
        StrategyIndex = strategyIndex;
        Attempt = attempt;
        Duration = duration;
        Handled = handled;
        _outcome = outcome;
        HasOutcome = hasOutcome;
        _context = context;
    }

    /// <summary>The event identity.</summary>
    public KevlarEventKind Kind { get; }

    /// <summary>The suggested event severity.</summary>
    public KevlarEventSeverity Severity { get; }

    /// <summary>The shield name, or <see langword="null"/> for an unnamed shield.</summary>
    public string? ShieldName => _context?.ShieldName;

    /// <summary>The bounded strategy identity, or <see cref="KevlarStrategyKind.None"/> for execution events.</summary>
    public KevlarStrategyKind StrategyKind { get; }

    /// <summary>The zero-based pipeline position, or -1 for execution events.</summary>
    public int StrategyIndex { get; }

    /// <summary>The zero-based attempt number.</summary>
    public int Attempt { get; }

    /// <summary>Elapsed time associated with this event; zero when no duration applies.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Whether a strategy handled the associated outcome.</summary>
    public bool Handled { get; }

    /// <summary>Whether this event carries an <see cref="Outcome"/>.</summary>
    public bool HasOutcome { get; }

    /// <summary>A bounded classification that does not expose or format result values.</summary>
    public KevlarOutcomeClassification OutcomeClassification => !HasOutcome
        ? KevlarOutcomeClassification.None
        : _outcome.IsSuccess
            ? KevlarOutcomeClassification.Success
            : _outcome.Exception is OperationCanceledException
                ? KevlarOutcomeClassification.Canceled
                : KevlarOutcomeClassification.Failure;

    /// <summary>
    /// The typed outcome. Access is valid only when <see cref="HasOutcome"/> is
    /// <see langword="true"/>.
    /// </summary>
    public Outcome<T> Outcome => HasOutcome
        ? _outcome
        : throw new InvalidOperationException("This event does not carry an outcome.");

    /// <summary>
    /// The pooled execution context. Read it only during the listener callback; do not mutate or
    /// retain it or its properties.
    /// </summary>
    public KevlarContext Context => _context
        ?? throw new InvalidOperationException("The event is not initialized.");

    /// <summary>Attempts to retrieve the typed outcome without boxing its result.</summary>
    public bool TryGetOutcome(out Outcome<T> outcome)
    {
        outcome = _outcome;
        return HasOutcome;
    }
}
