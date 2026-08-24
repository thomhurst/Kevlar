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
    public bool ShouldHandle<T>(in Outcome<T> outcome) => Judge.ShouldHandle(in outcome);
}
