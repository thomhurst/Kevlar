namespace Kevlar.Tests;

public class BulkheadTests
{
    [Test]
    public async Task Uncontended_Async_Execution_Completes_Synchronously()
    {
        var shield = Shield.ConcurrencyLimit(maxConcurrency: 1);

        var first = shield.ExecuteAsync(
            static _ => new ValueTask<int>(42));

        await Assert.That(first.IsCompletedSuccessfully).IsTrue();
        await Assert.That(await first).IsEqualTo(42);

        var afterRelease = shield.ExecuteAsync(
            static _ => new ValueTask<int>(43));
        await Assert.That(afterRelease.IsCompletedSuccessfully).IsTrue();
        await Assert.That(await afterRelease).IsEqualTo(43);
    }

    [Test]
    public async Task Rejects_Invalid_Limits_With_Descriptive_Errors()
    {
        await Assert.That(() => Shield.ConcurrencyLimit(maxConcurrency: 0))
            .Throws<ArgumentOutOfRangeException>()
            .WithMessage("MaxConcurrency must be positive. (Parameter 'maxConcurrency')");

        await Assert.That(() => Shield.ConcurrencyLimit(maxConcurrency: 1, queueLimit: -1))
            .Throws<ArgumentOutOfRangeException>()
            .WithMessage("QueueLimit must not be negative. (Parameter 'queueLimit')");
    }

    [Test]
    public async Task ConcurrencyLimit_Zero_QueueLimit_Rejects_Immediately()
    {
        var shield = Shield.ConcurrencyLimit(maxConcurrency: 1);
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var first = shield.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await started.Task;

        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<ConcurrencyLimitExceededException>();

        gate.SetResult();
        await Assert.That(await first).IsEqualTo(1);

        var afterRelease = await shield.ExecuteAsync(_ => new ValueTask<int>(3));
        await Assert.That(afterRelease).IsEqualTo(3);
    }

    [Test]
    public async Task ConcurrencyLimit_QueueLimit_Admits_Exactly_N_Waiters()
    {
        var shield = Shield.ConcurrencyLimit(maxConcurrency: 1, queueLimit: 1);
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var first = shield.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await started.Task;

        var queued = shield.ExecuteAsync(_ => new ValueTask<int>(2)).AsTask();
        await Assert.That(queued.IsCompleted).IsFalse();

        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(3)))
            .Throws<ConcurrencyLimitExceededException>();

        gate.SetResult();

        await Assert.That(await first).IsEqualTo(1);
        await Assert.That(await queued).IsEqualTo(2);
    }
}
