namespace Kevlar.Testing;

/// <summary>An immutable, versioned snapshot of a shield's observable strategy state.</summary>
public sealed class ShieldStateSnapshot
{
    internal ShieldStateSnapshot(IReadOnlyList<StrategyStateSnapshot> strategies)
    {
        Strategies = strategies;
    }

    /// <summary>Gets the snapshot contract version.</summary>
    public int ContractVersion => 1;

    /// <summary>Gets state snapshots in pipeline order. Stateless strategies are omitted.</summary>
    public IReadOnlyList<StrategyStateSnapshot> Strategies { get; }
}
