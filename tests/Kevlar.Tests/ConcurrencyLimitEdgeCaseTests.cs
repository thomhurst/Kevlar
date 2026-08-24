namespace Kevlar.Tests;

public class BulkheadEdgeCaseTests
{
    [Test]
    public async Task Failed_Executions_Release_Their_Slot()
    {
        var shield = Shield.ConcurrencyLimit(maxConcurrency: 1);

        for (var i = 0; i < 5; i++)
        {
            await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
                .Throws<InvalidOperationException>();
        }

        // If any failure leaked its slot, this would be rejected.
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var running = shield.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await started.Task;

        // Capacity is still exactly one: a concurrent execution is rejected.
        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<ConcurrencyLimitExceededException>();

        gate.SetResult();
        await Assert.That(await running).IsEqualTo(1);
    }

    [Test]
    public async Task Cancelling_A_Queued_Execution_Frees_Its_Queue_Slot()
    {
        var shield = Shield.ConcurrencyLimit(maxConcurrency: 1, queueLimit: 1);
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        using var cancellation = new CancellationTokenSource();

        var first = shield.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await started.Task;

        var queued = shield.ExecuteAsync(_ => new ValueTask<int>(2), cancellation.Token).AsTask();
        await Assert.That(queued.IsCompleted).IsFalse();

        // Queue is full.
        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(3)))
            .Throws<ConcurrencyLimitExceededException>();

        cancellation.Cancel();
        await Assert.That(async () => await queued).Throws<OperationCanceledException>();

        // The cancelled waiter released its queue slot: a new execution can queue again.
        var requeued = shield.ExecuteAsync(_ => new ValueTask<int>(4)).AsTask();
        await Assert.That(requeued.IsCompleted).IsFalse();

        gate.SetResult();
        await Assert.That(await first).IsEqualTo(1);
        await Assert.That(await requeued).IsEqualTo(4);
    }

    [Test]
    public async Task Concurrency_Is_Never_Exceeded_Under_Parallel_Load()
    {
        const int MaxConcurrency = 3;
        var shield = Shield.ConcurrencyLimit(MaxConcurrency, queueLimit: 50);
        var current = 0;
        var peak = 0;
        var barrier = new AsyncBarrier("maximum concurrent executions", MaxConcurrency);

        var tasks = Enumerable.Range(0, 40).Select(_ => shield.ExecuteAsync(async _ =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref peak, now);
            await barrier.SignalAndWaitAsync();
            Interlocked.Decrement(ref current);
            return 0;
        }).AsTask()).ToArray();

        await barrier.WaitForAllAsync();
        await Assert.That(Volatile.Read(ref peak)).IsEqualTo(MaxConcurrency);
        barrier.Release();
        await Task.WhenAll(tasks);

        await Assert.That(Volatile.Read(ref peak) <= MaxConcurrency).IsTrue();
    }

    [Test]
    public async Task Overload_Beyond_Capacity_Rejects_Exactly_The_Overflow()
    {
        var shield = Shield.ConcurrencyLimit(maxConcurrency: 2, queueLimit: 3);
        var barrier = new AsyncBarrier("both concurrency slots", 2);

        // Fill both concurrency slots.
        var running = Enumerable.Range(0, 2).Select(_ => shield.ExecuteAsync(async _ =>
        {
            await barrier.SignalAndWaitAsync();
            return 1;
        }).AsTask()).ToArray();

        await barrier.WaitForAllAsync();

        // Fill the queue.
        var queued = Enumerable.Range(0, 3).Select(_ => shield.ExecuteAsync(_ => new ValueTask<int>(2)).AsTask()).ToArray();

        // Everything beyond concurrency + queue is rejected immediately.
        var rejections = 0;
        for (var i = 0; i < 4; i++)
        {
            var outcome = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(3));
            if (outcome.Exception is ConcurrencyLimitExceededException)
            {
                rejections++;
            }
        }

        await Assert.That(rejections).IsEqualTo(4);

        barrier.Release();
        await Task.WhenAll(running);
        await Task.WhenAll(queued);
    }

    [Test]
    public async Task Bulkhead_State_Is_Shared_Across_Composed_Policies()
    {
        var limiter = Shield.ConcurrencyLimit(maxConcurrency: 1);
        var policyA = Shield.Retry(0, Backoff.None).Wrap(limiter);
        var policyB = Shield.Timeout(TimeSpan.FromMinutes(1)).Wrap(limiter);

        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var viaA = policyA.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await started.Task;

        // The same limiter slot, taken through shield A, rejects executions through shield B.
        await Assert.That(async () => await policyB.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<ConcurrencyLimitExceededException>();

        gate.SetResult();
        await Assert.That(await viaA).IsEqualTo(1);
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int snapshot;
        while (value > (snapshot = Volatile.Read(ref location)))
        {
            if (Interlocked.CompareExchange(ref location, value, snapshot) == snapshot)
            {
                return;
            }
        }
    }
}
