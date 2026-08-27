using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// Decides whether an outcome is a handled failure. Custom reactive strategies receive the
/// active clause through a <c>Use</c> factory and should consult it instead of duplicating filters.
/// </summary>
public readonly struct HandlingClause
{
    private readonly OutcomeJudge? _judge;

    internal HandlingClause(OutcomeJudge judge) => _judge = judge;

    /// <summary>
    /// The default handling clause: ordinary errors, excluding cancellation, Kevlar fail-fast
    /// rejections, and fatal runtime exceptions.
    /// </summary>
    public static HandlingClause Default { get; } = new(OutcomeJudge.Default);

    internal OutcomeJudge Judge => _judge ?? OutcomeJudge.Default;

    /// <summary>Returns whether <paramref name="outcome"/> is a handled failure.</summary>
    /// <remarks>
    /// This context-free overload cannot report predicate failures through execution diagnostics.
    /// Reactive strategies should use the overload that accepts <see cref="KevlarContext"/>.
    /// </remarks>
    public bool ShouldHandle<T>(in Outcome<T> outcome) =>
        Judge.ShouldHandle(in outcome, context: null, attempt: 0, strategyIndex: -1);

    /// <summary>Returns whether an outcome is handled for the active execution and strategy.</summary>
    public bool ShouldHandle<T>(
        in Outcome<T> outcome,
        KevlarContext context,
        int attemptNumber = 0,
        int strategyIndex = -1)
    {
        Throw.IfNull(context, nameof(context));
        return Judge.ShouldHandle(in outcome, context, attemptNumber, strategyIndex);
    }

    /// <summary>Whether this clause consults execution or strategy context.</summary>
    public bool IsContextAware => Judge.IsContextAware;
}
