namespace Kevlar.AllocationTests;

[NotInParallel]
public class AllocationBudgetTests
{
    private const int WarmupOperations = 10_000;
    private const int MeasuredOperations = 10_000;
    private const int Samples = 5;

    private static readonly InvalidOperationException RecoverableFailure = new("recoverable");

    private readonly Shield _empty = Shield.Empty;
    private readonly Shield _retry = Shield.Retry(3, Backoff.None);
    private readonly Shield _breaker = Shield.CircuitBreaker(5, TimeSpan.FromMinutes(1));
    private readonly Shield _timeout = Shield.Timeout(TimeSpan.FromMinutes(1));
    private readonly Shield<int> _fallback = Shield.For<int>()
        .When<InvalidOperationException>()
        .Fallback(7);
    private readonly Shield _rateLimit = Shield.RateLimit(1_000_000_000, TimeSpan.FromSeconds(1));
    private readonly Shield _concurrencyLimit = Shield.ConcurrencyLimit(1024);
    private readonly Shield<int> _typedJudge = Shield.For<int>()
        .WhenResult(-1)
        .Retry(3, Backoff.None);
    private readonly Shield _composed = Shield
        .RateLimit(1_000_000_000, TimeSpan.FromSeconds(1))
        .Timeout(TimeSpan.FromMinutes(1))
        .Retry(3, Backoff.None)
        .CircuitBreaker(5, TimeSpan.FromMinutes(1))
        .ConcurrencyLimit(1024);
    private readonly Shield<int> _primaryWinsHedge = Shield.For<int>()
        .Hedge(2, TimeSpan.FromMinutes(1));

    private readonly Shield _recoveryRetry = Shield.Retry(3, Backoff.None);
    private readonly Shield _openBreaker = Shield.CircuitBreaker(1, TimeSpan.FromDays(1));
    private readonly Shield<int> _triggeredFallback = Shield.For<int>()
        .When<InvalidOperationException>()
        .Fallback(7);
    private readonly Shield _parallelHedge = Shield.Hedge(2, TimeSpan.Zero);
    private readonly Counter _retryCounter = new();
    private readonly Counter _hedgeCounter = new();

    [Test]
    public void Documented_Hot_Paths_Allocate_Zero_Bytes_Per_Operation()
    {
        AssertZero("empty sync", this, static test => test._empty.Execute(static _ => 42));
        AssertZero("empty async", this, static test =>
            test._empty.ExecuteAsync(static _ => new ValueTask<int>(42)).GetAwaiter().GetResult());
        AssertZero("empty async state", this, static test =>
            test._empty.ExecuteAsync(42, static (state, _) => new ValueTask<int>(state)).GetAwaiter().GetResult());
        AssertZero("retry sync happy path", this, static test =>
            test._retry.Execute(static _ => 42));
        AssertZero("retry async happy path", this, static test =>
            test._retry.ExecuteAsync(static _ => new ValueTask<int>(42)).GetAwaiter().GetResult());
        AssertZero("circuit closed", this, static test =>
            test._breaker.ExecuteAsync(static _ => new ValueTask<int>(42)).GetAwaiter().GetResult());
        AssertZero("timeout happy path", this, static test =>
            test._timeout.ExecuteAsync(static _ => new ValueTask<int>(42)).GetAwaiter().GetResult());
        AssertZero("fallback pass-through", this, static test =>
            test._fallback.ExecuteAsync(static _ => new ValueTask<int>(42)).GetAwaiter().GetResult());
        AssertZero("rate limit uncontended", this, static test =>
            test._rateLimit.ExecuteAsync(static _ => new ValueTask<int>(42)).GetAwaiter().GetResult());
        AssertZero("concurrency limit uncontended", this, static test =>
            test._concurrencyLimit.ExecuteAsync(static _ => new ValueTask<int>(42)).GetAwaiter().GetResult());
        AssertZero("typed result judging", this, static test =>
            test._typedJudge.ExecuteAsync(static _ => new ValueTask<int>(42)).GetAwaiter().GetResult());
        AssertZero("composed pipeline", this, static test =>
            test._composed.ExecuteAsync(static _ => new ValueTask<int>(42)).GetAwaiter().GetResult());
        AssertZero("hedge primary wins", this, static test =>
            test._primaryWinsHedge.ExecuteAsync(static _ => new ValueTask<int>(42)).GetAwaiter().GetResult());
    }

    [Test]
    public void Allocating_Paths_Stay_Within_Per_Operation_Budgets()
    {
        _openBreaker.ExecuteOutcomeAsync<int>(static _ => throw RecoverableFailure).GetAwaiter().GetResult();

        AssertBudget("retry recovers after two failures", 512, this, static test =>
            test._recoveryRetry.ExecuteAsync(test._retryCounter, static (counter, _) =>
            {
                if (++counter.Value % 3 != 0)
                {
                    throw RecoverableFailure;
                }

                return new ValueTask<int>(42);
            }).GetAwaiter().GetResult());
        AssertBudget("fallback triggered", 512, this, static test =>
            test._triggeredFallback.ExecuteAsync(static _ => throw RecoverableFailure).GetAwaiter().GetResult());
        AssertBudget("open circuit rejection", 2_048, this, static test =>
        {
            try
            {
                test._openBreaker.ExecuteAsync(static _ => new ValueTask<int>(42)).GetAwaiter().GetResult();
            }
            catch (CircuitOpenException)
            {
            }
        });
        AssertBudget("hedge launches second attempt", 2_048, this, static test =>
            test._parallelHedge.ExecuteAsync(test._hedgeCounter, static (counter, _) =>
            {
                if (++counter.Value % 2 != 0)
                {
                    throw RecoverableFailure;
                }

                return new ValueTask<int>(42);
            }).GetAwaiter().GetResult());
    }

    private static void AssertZero<TState>(string scenario, TState state, Action<TState> operation) =>
        AssertBudget(scenario, 0, state, operation);

    private static void AssertBudget<TState>(
        string scenario,
        long maximumBytesPerOperation,
        TState state,
        Action<TState> operation)
    {
        for (var operationIndex = 0; operationIndex < WarmupOperations; operationIndex++)
        {
            operation(state);
        }

        var maximumObserved = 0L;
        for (var sample = 0; sample < Samples; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var operationIndex = 0; operationIndex < MeasuredOperations; operationIndex++)
            {
                operation(state);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            maximumObserved = Math.Max(maximumObserved, allocated);
        }

        var maximumAllowed = maximumBytesPerOperation * MeasuredOperations;
        Console.WriteLine(
            $"{scenario}: {maximumObserved / (double)MeasuredOperations:F2} B/op " +
            $"(budget {maximumBytesPerOperation} B/op)");

        if (maximumObserved > maximumAllowed)
        {
            throw new InvalidOperationException(
                $"{scenario} allocated {maximumObserved / (double)MeasuredOperations:F2} B/op; " +
                $"budget is {maximumBytesPerOperation} B/op.");
        }
    }

    private sealed class Counter
    {
        public int Value;
    }
}
