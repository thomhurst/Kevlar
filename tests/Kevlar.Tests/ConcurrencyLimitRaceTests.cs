namespace Kevlar.Tests;

public class ConcurrencyLimitRaceTests
{
    [Test]
    public async Task Exact_Capacity_Is_Admitted_And_Overflow_Is_Rejected()
    {
        const int MaxConcurrency = 3;
        const int MaxQueue = 5;
        const int Overflow = 7;
        var shield = Shield.ConcurrencyLimit(MaxConcurrency, MaxQueue);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runningStarted = new AsyncCounter("running concurrency-limit calls");
        var queuedStarted = new AsyncCounter("queued concurrency-limit calls");
        var inFlight = 0;
        var peak = 0;

        var running = Enumerable.Range(0, MaxConcurrency)
            .Select(index => shield.ExecuteAsync(async _ =>
            {
                Enter(ref inFlight, ref peak, MaxConcurrency);
                runningStarted.Signal();
                try
                {
                    await release.Task;
                    return index;
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                }
            }).AsTask())
            .ToArray();

        await runningStarted.WaitForAsync(MaxConcurrency);

        var queued = Enumerable.Range(0, MaxQueue)
            .Select(index => shield.ExecuteAsync(_ =>
            {
                Enter(ref inFlight, ref peak, MaxConcurrency);
                queuedStarted.Signal();
                Interlocked.Decrement(ref inFlight);
                return new ValueTask<int>(index);
            }).AsTask())
            .ToArray();

        var overflow = await Task.WhenAll(Enumerable.Range(0, Overflow)
            .Select(_ => shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(-1)).AsTask()));

        await Assert.That(overflow.Count(outcome => outcome.Exception is ConcurrencyLimitExceededException))
            .IsEqualTo(Overflow);
        await Assert.That(queuedStarted.Count).IsEqualTo(0);

        release.SetResult();
        await Task.WhenAll(running);
        await Task.WhenAll(queued);
        await queuedStarted.WaitForAsync(MaxQueue);

        await Assert.That(Volatile.Read(ref peak)).IsEqualTo(MaxConcurrency);
        await Assert.That(Volatile.Read(ref inFlight)).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Grant_And_Queued_Cancellation_Decrement_Accounting_Once(bool grantFirst)
    {
        var shield = Shield.ConcurrencyLimit(maxConcurrency: 1, maxQueue: 1);
        var releaseRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runningStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueQueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var running = shield.ExecuteAsync(async _ =>
        {
            runningStarted.SetResult();
            await releaseRunning.Task;
            return 1;
        }).AsTask();
        await runningStarted.Task;

        var queued = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            queuedStarted.SetResult();
            await continueQueued.Task;
            token.ThrowIfCancellationRequested();
            return 2;
        }, cancellation.Token).AsTask();

        if (grantFirst)
        {
            releaseRunning.SetResult();
            await queuedStarted.Task;
            cancellation.Cancel();
            continueQueued.SetResult();
        }
        else
        {
            cancellation.Cancel();
            var cancelledBeforeGrant = await queued;
            await Assert.That(cancelledBeforeGrant.Exception).IsTypeOf<OperationCanceledException>();
            await Assert.That(queuedStarted.Task.IsCompleted).IsFalse();
            releaseRunning.SetResult();
        }

        await Assert.That(await running).IsEqualTo(1);
        var outcome = await queued;
        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken == cancellation.Token)
            .IsTrue();

        var probe = await shield.ExecuteAsync(_ => new ValueTask<int>(3));
        await Assert.That(probe).IsEqualTo(3);
    }

    [Test]
    public async Task Mixed_Executions_Drain_And_Reuse_Full_Capacity_Every_Round()
    {
        const int MaxConcurrency = 2;
        const int Rounds = 32;
        var shield = Shield.ConcurrencyLimit(MaxConcurrency, maxQueue: 3);
        var inFlight = 0;
        var peak = 0;

        for (var round = 0; round < Rounds; round++)
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new AsyncCounter($"round {round} running calls");

            var running = Enumerable.Range(0, MaxConcurrency)
                .Select(_ => shield.ExecuteAsync(async _ =>
                {
                    Enter(ref inFlight, ref peak, MaxConcurrency);
                    started.Signal();
                    try
                    {
                        await release.Task;
                        return round;
                    }
                    finally
                    {
                        Interlocked.Decrement(ref inFlight);
                    }
                }).AsTask())
                .ToArray();
            await started.WaitForAsync(MaxConcurrency);

            var success = shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(round)).AsTask();
            var failure = shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException("expected")).AsTask();
            using var cancellation = new CancellationTokenSource();
            var cancelled = shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(-1), cancellation.Token).AsTask();

            cancellation.Cancel();
            await Assert.That((await cancelled).Exception).IsTypeOf<OperationCanceledException>();

            var replacement = shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(round)).AsTask();
            release.SetResult();

            await Task.WhenAll(running);
            await Assert.That((await success).Result).IsEqualTo(round);
            await Assert.That((await failure).Exception).IsTypeOf<InvalidOperationException>();
            await Assert.That((await replacement).Result).IsEqualTo(round);

            var synchronous = shield.Execute(round, static (value, _) => value);
            await Assert.That(synchronous).IsEqualTo(round);
            await Assert.That(() => shield.Execute<int>(_ => throw new InvalidOperationException("sync expected")))
                .Throws<InvalidOperationException>();

            using var synchronousCancellation = new CancellationTokenSource();
            synchronousCancellation.Cancel();
            await Assert.That(() => shield.Execute(_ => 0, synchronousCancellation.Token))
                .Throws<OperationCanceledException>();
            await Assert.That(Volatile.Read(ref inFlight)).IsEqualTo(0);
        }

        await Assert.That(Volatile.Read(ref peak)).IsEqualTo(MaxConcurrency);
    }

    [Test]
    public async Task Derived_And_Composed_Copies_Share_Running_And_Queue_Accounting()
    {
        var limiter = Shield.ConcurrencyLimit(maxConcurrency: 1, maxQueue: 1);
        var named = limiter.WithName("named");
        var composed = Shield.Retry(0, Backoff.None).Wrap(limiter);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = named.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await release.Task;
            return 1;
        }).AsTask();
        await started.Task;

        var queued = composed.ExecuteAsync(_ => new ValueTask<int>(2)).AsTask();
        await Assert.That(queued.IsCompleted).IsFalse();
        await Assert.That(async () => await limiter.ExecuteAsync(_ => new ValueTask<int>(3)))
            .Throws<ConcurrencyLimitExceededException>();

        release.SetResult();
        await Assert.That(await running).IsEqualTo(1);
        await Assert.That(await queued).IsEqualTo(2);
    }

    [Test]
    [Arguments(int.MaxValue, 0)]
    [Arguments(int.MaxValue - 1, 1)]
    public async Task Accepted_Capacity_Arithmetic_Near_Int_MaxValue_Remains_Usable(
        int maxConcurrency,
        int maxQueue)
    {
        var shield = Shield.ConcurrencyLimit(maxConcurrency, maxQueue);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
    }

    private static void Enter(ref int inFlight, ref int peak, int maxConcurrency)
    {
        var current = Interlocked.Increment(ref inFlight);
        int snapshot;
        while (current > (snapshot = Volatile.Read(ref peak)))
        {
            if (Interlocked.CompareExchange(ref peak, current, snapshot) == snapshot)
            {
                break;
            }
        }

        if (current > maxConcurrency)
        {
            throw new InvalidOperationException($"Concurrency limit exceeded: {current}.");
        }
    }
}
