namespace Kevlar.Testing;

/// <summary>Immutable, read-only description of a shield pipeline.</summary>
public sealed class ShieldDescriptor
{
    internal ShieldDescriptor(
        string? name,
        Type? resultType,
        bool usesCustomTimeProvider,
        IReadOnlyList<StrategyDescriptor> strategies)
    {
        Name = name;
        ResultType = resultType;
        UsesCustomTimeProvider = usesCustomTimeProvider;
        Strategies = strategies;
    }

    /// <summary>The shield name, or <see langword="null"/> for an unnamed shield.</summary>
    public string? Name { get; }

    /// <summary>The result type for a typed shield, or <see langword="null"/> for an untyped shield.</summary>
    public Type? ResultType { get; }

    /// <summary>Whether the shield has an explicitly configured <see cref="TimeProvider"/>.</summary>
    public bool UsesCustomTimeProvider { get; }

    /// <summary>Strategies in execution order, outermost first.</summary>
    public IReadOnlyList<StrategyDescriptor> Strategies { get; }
}
