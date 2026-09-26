namespace Kevlar.Testing;

/// <summary>How often a timeout budget is established within one shield execution.</summary>
public enum TimeoutScope
{
    /// <summary>The strategy is not a timeout.</summary>
    None,
    /// <summary>One budget surrounds the downstream pipeline for the execution.</summary>
    Execution,
    /// <summary>A fresh budget surrounds the downstream pipeline on each enclosing retry or hedge attempt.</summary>
    Attempt,
    /// <summary>An enclosing custom strategy makes the invocation scope unknown.</summary>
    Indeterminate,
}
