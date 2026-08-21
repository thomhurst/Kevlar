namespace Kevlar.Testing;

/// <summary>Base type for an immutable snapshot of one stateful strategy.</summary>
public abstract class StrategyStateSnapshot
{
    private protected StrategyStateSnapshot(StrategyKind kind, int strategyIndex)
    {
        Kind = kind;
        StrategyIndex = strategyIndex;
    }

    /// <summary>Gets the strategy kind.</summary>
    public StrategyKind Kind { get; }

    /// <summary>Gets the zero-based position of the strategy in the shield.</summary>
    public int StrategyIndex { get; }
}
