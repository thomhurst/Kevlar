namespace Kevlar.Testing;

/// <summary>The source of a strategy's effective outcome handling.</summary>
public enum HandlingSource
{
    /// <summary>The strategy has no reactive handling clause.</summary>
    None,
    /// <summary>The strategy uses its built-in default handling.</summary>
    Default,
    /// <summary>The strategy inherits an explicitly configured ambient clause.</summary>
    Ambient,
    /// <summary>Strategy-local predicates replace the ambient clause.</summary>
    LocalOverride,
    /// <summary>A custom strategy supplies its own declared handling metadata.</summary>
    Custom,
}
