namespace Kevlar;

/// <summary>Context supplied to an exception handling predicate.</summary>
public readonly struct HandlingEvent
{
    internal HandlingEvent(Exception exception, KevlarContext context, int attempt, int strategyIndex)
    {
        Exception = exception;
        Context = context;
        Attempt = attempt;
        StrategyIndex = strategyIndex;
    }

    /// <summary>The exception being classified.</summary>
    public Exception Exception { get; }

    /// <summary>The active execution context. Do not retain this pooled object.</summary>
    public KevlarContext Context { get; }

    /// <summary>The zero-based attempt number for retry and hedging; zero for other strategies.</summary>
    public int Attempt { get; }

    /// <summary>The zero-based index of the strategy evaluating the outcome.</summary>
    public int StrategyIndex { get; }
}
