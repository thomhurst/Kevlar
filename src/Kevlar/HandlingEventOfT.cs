namespace Kevlar;

/// <summary>Context supplied to a result-aware handling predicate.</summary>
/// <typeparam name="TResult">The execution result type.</typeparam>
public readonly struct HandlingEvent<TResult>
{
    internal HandlingEvent(Outcome<TResult> outcome, KevlarContext context, int attempt, int strategyIndex)
    {
        Outcome = outcome;
        Context = context;
        Attempt = attempt;
        StrategyIndex = strategyIndex;
    }

    /// <summary>The exception or result being classified.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>The active execution context. Do not retain this pooled object.</summary>
    public KevlarContext Context { get; }

    /// <summary>The zero-based attempt number for retry and hedging; zero for other strategies.</summary>
    public int Attempt { get; }

    /// <summary>The zero-based index of the strategy evaluating the outcome.</summary>
    public int StrategyIndex { get; }
}
