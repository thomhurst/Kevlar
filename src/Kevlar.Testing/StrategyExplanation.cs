namespace Kevlar.Testing;

/// <summary>Effective behavior of one strategy, at its outer-to-inner descriptor position.</summary>
public sealed class StrategyExplanation
{
    internal StrategyExplanation(
        int index,
        StrategyDescriptor strategy,
        HandlingSource handlingSource,
        string? handlingDescription,
        TimeoutScope timeoutScope,
        IReadOnlyList<int> enclosingAttemptStrategies)
    {
        Index = index;
        Strategy = strategy;
        HandlingSource = handlingSource;
        HandlingDescription = handlingDescription;
        TimeoutScope = timeoutScope;
        EnclosingAttemptStrategies = enclosingAttemptStrategies;
    }

    /// <summary>Zero-based position in the descriptor, outermost first.</summary>
    public int Index { get; }
    /// <summary>The original immutable strategy descriptor, including dynamic-generator flags.</summary>
    public StrategyDescriptor Strategy { get; }
    /// <summary>The source of effective handling, without evaluating its predicates.</summary>
    public HandlingSource HandlingSource { get; }
    /// <summary>Effective clause text or an explanation of opaque/default handling; null for proactive strategies.</summary>
    public string? HandlingDescription { get; }
    /// <summary>The timeout's invocation scope, or <see cref="Kevlar.Testing.TimeoutScope.None"/> for other strategies.</summary>
    public TimeoutScope TimeoutScope { get; }
    /// <summary>
    /// Descriptor indexes of enclosing retry/hedge strategies that can repeat this strategy.
    /// A timeout covers the entire suffix after its index, including any inner retry/hedge groups.
    /// Custom enclosing strategies are represented by an indeterminate timeout scope instead.
    /// </summary>
    public IReadOnlyList<int> EnclosingAttemptStrategies { get; }
}
