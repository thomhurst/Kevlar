namespace Kevlar.Testing;

/// <summary>Read-only description of one strategy in a shield pipeline.</summary>
public abstract class StrategyDescriptor
{
    internal StrategyDescriptor(StrategyKind kind, string description)
    {
        Kind = kind;
        Description = description;
    }

    /// <summary>The stable strategy category.</summary>
    public StrategyKind Kind { get; }

    /// <summary>A human-readable diagnostic description. Do not parse this value as a contract.</summary>
    public string Description { get; }

    /// <summary>The strategy's handling metadata, or <see langword="null"/> when proactive.</summary>
    public HandlingClauseDescriptor? HandlingClause { get; private set; }

    internal void SetHandlingClause(HandlingClauseDescriptor? handlingClause) =>
        HandlingClause = handlingClause;
}
