namespace Kevlar.Testing;

/// <summary>Diagnostic description of a caller-defined strategy.</summary>
public sealed class CustomStrategyDescriptor : StrategyDescriptor
{
    internal CustomStrategyDescriptor(string description, Type strategyType)
        : base(StrategyKind.Custom, description) => StrategyType = strategyType;

    /// <summary>The caller-defined strategy type.</summary>
    public Type StrategyType { get; }
}
