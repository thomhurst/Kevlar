using BenchmarkDotNet.Attributes;

namespace Kevlar.Benchmarks;

/// <summary>Opt-in allowance costs with enough tokens to avoid exhaustion during measured attempts.</summary>
[MemoryDiagnoser]
public class ReplenishingRetryBudgetBenchmarks
{
    private readonly Shield _retry = Shield.Retry(options =>
        options.Budget = RetryBudget.CreateReplenishing(int.MaxValue, replenishmentPeriod: TimeSpan.FromSeconds(1)));
    private readonly Shield<int> _hedge = Shield.For<int>().Hedge(options =>
        options.Budget = RetryBudget.CreateReplenishing(int.MaxValue, replenishmentPeriod: TimeSpan.FromSeconds(1)));
    private readonly Shield<int> _recovery = Shield.For<int>().Retry(options =>
    {
        options.Budget = RetryBudget.CreateReplenishing(int.MaxValue, replenishmentPeriod: TimeSpan.FromSeconds(1));
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
