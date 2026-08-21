using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class ConcurrencyTests
{
    [Test]
    public async Task A_Failure_Storm_Produces_Exactly_One_Open_Transition()
    {
        var transitions = new List<CircuitStateChangedEvent>();
        var gate = new object();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 3;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.OnStateChanged = change =>
            {
                lock (gate)
                {
                    transitions.Add(change);
                }
            };
        });

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException()).AsTask()));

        List<CircuitStateChangedEvent> snapshot;
        lock (gate)
        {
            snapshot = [.. transitions];
        }

        await Assert.That(snapshot.Count(change => change.To == CircuitState.Open)).IsEqualTo(1);
        await Assert.That(snapshot.Count(change => change.From == CircuitState.Closed)).IsEqualTo(1);
    }

    [Test]
    public async Task A_Shared_Retry_Strategy_Is_Safe_Across_Parallel_Executions()
    {
        var shield = Shield.Retry(1, Backoff.None);
        var failFirst = new bool[64];

        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(i =>
            shield.ExecuteAsync(i, (index, _) =>
            {
                if (!failFirst[index])
                {
                    failFirst[index] = true;
                    throw new InvalidOperationException();
                }

                return new ValueTask<int>(index);
            }).AsTask()));

        // Every execution failed once, retried once, and got its own result back.
        for (var i = 0; i < 64; i++)
        {
            await Assert.That(results[i]).IsEqualTo(i);
        }
    }

    [Test]
    public async Task Parallel_Executions_Do_Not_Share_Context_Properties()
    {
        var key = new KevlarKey<int>("execution-id");
        var mismatches = 0;

        var typed = Shield.For<int>()
            .WhenResult(value => value >= 0)
            .Retry(options =>
            {
                options.MaxRetries = 2;
                options.Backoff = Backoff.None;
                options.OnRetry = retry =>
                {
                    if (retry.Attempt == 1)
                    {
                        retry.Context.Properties.Set(key, retry.Outcome.Result);
                    }
                    else if (retry.Context.Properties.GetOrDefault(key, -1) != retry.Outcome.Result)
                    {
                        Interlocked.Increment(ref mismatches);
                    }
                };
            });

        await Task.WhenAll(Enumerable.Range(0, 32).Select(i =>
            typed.ExecuteAsync(async _ =>
            {
                await Task.Yield();
                return i;
            }).AsTask()));

        await Assert.That(Volatile.Read(ref mismatches)).IsEqualTo(0);
    }

    [Test]
    public async Task Concurrent_Executions_Through_An_Open_Circuit_All_Reject()
    {
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromMinutes(1));

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        var invoked = 0;
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            shield.ExecuteOutcomeAsync(_ =>
            {
                Interlocked.Increment(ref invoked);
                return new ValueTask<int>(1);
            }).AsTask()));

        await Assert.That(Volatile.Read(ref invoked)).IsEqualTo(0);
        await Assert.That(outcomes.Count(outcome => outcome.Exception is CircuitOpenException)).IsEqualTo(32);
    }

    [Test]
    public async Task Parallel_HalfOpen_Races_Admit_Exactly_One_Probe()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(1)).WithTimeProvider(fakeTime);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        fakeTime.Advance(TimeSpan.FromSeconds(1));

        var probeGate = new TaskCompletionSource();
        var probes = 0;
        var probesStarted = new AsyncCounter("half-open probes");

        var outcomes = Enumerable.Range(0, 16).Select(_ =>
            shield.ExecuteOutcomeAsync(async _ =>
            {
                Interlocked.Increment(ref probes);
                probesStarted.Signal();
                await probeGate.Task;
                return 1;
            }).AsTask()).ToArray();

        // Exactly one execution won the probe slot; every other one was rejected immediately.
        await probesStarted.WaitForAsync(1);
        var rejected = outcomes.Count(task => task.IsCompletedSuccessfully && task.Result.Exception is CircuitOpenException);
        await Assert.That(rejected).IsEqualTo(15);

        probeGate.SetResult();
        var results = await Task.WhenAll(outcomes);
        await Assert.That(results.Count(outcome => outcome.IsSuccess)).IsEqualTo(1);
    }

    [Test]
    public async Task Sync_And_Async_Executions_Can_Share_One_Policy()
    {
        var shield = Shield.Retry(1, Backoff.None);
        var asyncResults = Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            shield.ExecuteAsync(i, static (index, _) => new ValueTask<int>(index)).AsTask()));

        var syncResults = new int[8];
        Parallel.For(0, 8, i => syncResults[i] = shield.Execute(i, static (index, _) => index));

        var fromAsync = await asyncResults;
        for (var i = 0; i < 8; i++)
        {
            await Assert.That(fromAsync[i]).IsEqualTo(i);
            await Assert.That(syncResults[i]).IsEqualTo(i);
        }
    }

    [Test]
    public async Task Immutable_Policies_Can_Be_Extended_Concurrently_With_Executions()
    {
        var basePolicy = Shield.Retry(1, Backoff.None);

        var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(async () =>
        {
            // Deriving new shields from a shared base must never disturb in-flight executions.
            var derived = basePolicy.Timeout(TimeSpan.FromMinutes(1)).WithName($"derived-{i}");
            return await derived.ExecuteAsync(i, static (index, _) => new ValueTask<int>(index));
        })).ToArray();

        var results = await Task.WhenAll(tasks);
        for (var i = 0; i < 16; i++)
        {
            await Assert.That(results[i]).IsEqualTo(i);
        }
    }
}
