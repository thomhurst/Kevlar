namespace Kevlar.Testing;

/// <summary>Diagnostic description of a caller-defined strategy.</summary>
public sealed class CustomStrategyDescriptor : StrategyDescriptor
{
    internal CustomStrategyDescriptor(string description, Type strategyType, HandlingClause? handling)
        : this(StrategyKind.Custom, description, strategyType, handling)
    {
    }

    internal CustomStrategyDescriptor(
        StrategyKind kind,
        string description,
        Type strategyType,
        HandlingClause? handling)
        : base(kind, description)
    {
        StrategyType = strategyType;
        Handling = handling;
    }

    /// <summary>The caller-defined strategy type.</summary>
    public Type StrategyType { get; }

    /// <summary>The strategy's declared handling clause, or <see langword="null"/> when proactive.</summary>
    public HandlingClause? Handling { get; }
}
