namespace Kevlar;

/// <summary>Well-known property keys consumed by Kevlar integrations.</summary>
public static class KevlarKeys
{
    /// <summary>
    /// A bounded logical operation identifier included in strategy telemetry when present.
    /// </summary>
    /// <remarks>
    /// Use a small controlled vocabulary. Never store request IDs, URIs, partition keys, or other
    /// unbounded values because metric dimensions must remain low-cardinality.
    /// </remarks>
    public static KevlarKey<string> OperationKey { get; } = new("kevlar.operation.key");

}
