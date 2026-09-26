using BenchmarkDotNet.Attributes;

namespace Kevlar.Benchmarks;

/// <summary>Steady-state cost of opt-in shared feedback, with successful traffic replenishing tokens.</summary>
[MemoryDiagnoser]
public class RetryBudgetBenchmarks
{
    private readonly Shield _retry = Shield.Retry(options => options.Budget = new RetryBudget());
    private readonly Shield<int> _hedge = Shield.For<int>().Hedge(options => options.Budget = new RetryBudget());
    private readonly Shield<int> _recovery = Shield.For<int>().Retry(options =>
    {
        options.Budget = new RetryBudget(maxTokens: 100, tokenRatio: 2);
        options.Backoff = Backoff.None;
        options.HandlesResult = static value => value == 0;
    });
    private int _attempt;

    [Benchmark]
    public ValueTask<int> RetrySuccess() => _retry.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> HedgePrimaryWins() => _hedge.ExecuteAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> HandledResultRecovery() => _recovery.ExecuteAsync(this,
        static (benchmark, _) => new ValueTask<int>(++benchmark._attempt % 3 == 0 ? 42 : 0));
}
