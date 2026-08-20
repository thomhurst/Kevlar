namespace Kevlar.Tests;

public class BulkheadEdgeCaseTests
{
    [Test]
    public async Task Failed_Executions_Release_Their_Slot()
    {
        var policy = Policy.Bulkhead(maxConcurrency: 1);

        for (var i = 0; i < 5; i++)
        {
            await Assert.That(async () => await policy.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
                .Throws<InvalidOperationException>();
        }

        // If any failure leaked its slot, this would be rejected.
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var running = policy.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await started.Task;

        // Capacity is still exactly one: a concurrent execution is rejected.
        await Assert.That(async () => await policy.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<BulkheadRejectedException>();

        gate.SetResult();
        await Assert.That(await running).IsEqualTo(1);
    }

    [Test]
    public async Task Cancelling_A_Queued_Execution_Frees_Its_Queue_Slot()
    {
        var policy = Policy.Bulkhead(maxConcurrency: 1, maxQueue: 1);
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        using var cancellation = new CancellationTokenSource();

        var first = policy.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await started.Task;

        var queued = policy.ExecuteAsync(_ => new ValueTask<int>(2), cancellation.Token).AsTask();
        await Assert.That(queued.IsCompleted).IsFalse();

        // Queue is full.
        await Assert.That(async () => await policy.ExecuteAsync(_ => new ValueTask<int>(3)))
            .Throws<BulkheadRejectedException>();

        cancellation.Cancel();
        await Assert.That(async () => await queued).Throws<OperationCanceledException>();

        // The cancelled waiter released its queue slot: a new execution can queue again.
        var requeued = policy.ExecuteAsync(_ => new ValueTask<int>(4)).AsTask();
        await Assert.That(requeued.IsCompleted).IsFalse();

        gate.SetResult();
        await Assert.That(await first).IsEqualTo(1);
        await Assert.That(await requeued).IsEqualTo(4);
    }

    [Test]
    public async Task Concurrency_Is_Never_Exceeded_Under_Parallel_Load()
    {
        const int MaxConcurrency = 3;
        var policy = Policy.Bulkhead(MaxConcurrency, maxQueue: 50);
        var current = 0;
        var peak = 0;

        var tasks = Enumerable.Range(0, 40).Select(_ => policy.ExecuteAsync(async _ =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref peak, now);
            await Task.Delay(10);
            Interlocked.Decrement(ref current);
            return 0;
        }).AsTask()).ToArray();

        await Task.WhenAll(tasks);

        await Assert.That(Volatile.Read(ref peak) <= MaxConcurrency).IsTrue();
        await Assert.That(Volatile.Read(ref peak) >= 1).IsTrue();
    }

    [Test]
    public async Task Overload_Beyond_Capacity_Rejects_Exactly_The_Overflow()
    {
        var policy = Policy.Bulkhead(maxConcurrency: 2, maxQueue: 3);
        var gate = new TaskCompletionSource();
        var startedCount = 0;

        // Fill both concurrency slots.
        var running = Enumerable.Range(0, 2).Select(_ => policy.ExecuteAsync(async _ =>
        {
            Interlocked.Increment(ref startedCount);
            await gate.Task;
            return 1;
        }).AsTask()).ToArray();

        await TestHelpers.WaitUntil(() => Volatile.Read(ref startedCount) == 2);

        // Fill the queue.
        var queued = Enumerable.Range(0, 3).Select(_ => policy.ExecuteAsync(_ => new ValueTask<int>(2)).AsTask()).ToArray();

        // Everything beyond concurrency + queue is rejected immediately.
        var rejections = 0;
        for (var i = 0; i < 4; i++)
        {
            var outcome = await policy.ExecuteOutcomeAsync(_ => new ValueTask<int>(3));
            if (outcome.Exception is BulkheadRejectedException)
            {
                rejections++;
            }
        }

        await Assert.That(rejections).IsEqualTo(4);

        gate.SetResult();
        await Task.WhenAll(running);
        await Task.WhenAll(queued);
    }

    [Test]
    public async Task Bulkhead_State_Is_Shared_Across_Composed_Policies()
    {
        var bulkhead = Policy.Bulkhead(maxConcurrency: 1);
        var policyA = Policy.Retry(0, Backoff.None).Wrap(bulkhead);
        var policyB = Policy.Timeout(TimeSpan.FromMinutes(1)).Wrap(bulkhead);

        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var viaA = policyA.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await started.Task;

        // The same bulkhead slot, taken through policy A, rejects executions through policy B.
        await Assert.That(async () => await policyB.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<BulkheadRejectedException>();

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
