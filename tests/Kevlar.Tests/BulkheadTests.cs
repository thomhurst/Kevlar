namespace Kevlar.Tests;

public class BulkheadTests
{
    [Test]
    public async Task Rejects_When_Concurrency_And_Queue_Are_Full()
    {
        var policy = Policy.Bulkhead(maxConcurrency: 1);
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var first = policy.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await started.Task;

        await Assert.That(async () => await policy.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<BulkheadRejectedException>();

        gate.SetResult();
        await Assert.That(await first).IsEqualTo(1);

        var afterRelease = await policy.ExecuteAsync(_ => new ValueTask<int>(3));
        await Assert.That(afterRelease).IsEqualTo(3);
    }

    [Test]
    public async Task Queued_Executions_Run_When_A_Slot_Frees()
    {
        var policy = Policy.Bulkhead(maxConcurrency: 1, maxQueue: 1);
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var first = policy.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await started.Task;

        var queued = policy.ExecuteAsync(_ => new ValueTask<int>(2)).AsTask();
        await Assert.That(queued.IsCompleted).IsFalse();

        await Assert.That(async () => await policy.ExecuteAsync(_ => new ValueTask<int>(3)))
            .Throws<BulkheadRejectedException>();

        gate.SetResult();

        await Assert.That(await first).IsEqualTo(1);
        await Assert.That(await queued).IsEqualTo(2);
    }
}
