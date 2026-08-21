using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class RateLimitCancellationTests
{
    [Test]
    public async Task Cancelled_Reservation_Is_Reused_At_Its_Original_Due_Time()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = CreateShield(fakeTime, queueLimit: 1);
        using var cancellation = new CancellationTokenSource();

        await shield.ExecuteAsync(_ => new ValueTask<int>(0));
        var cancelled = shield.ExecuteAsync(_ => new ValueTask<int>(1), cancellation.Token).AsTask();

        cancellation.Cancel();
        await Assert.That(async () => await cancelled).Throws<OperationCanceledException>();

        var replacement = shield.ExecuteAsync(_ => new ValueTask<int>(2)).AsTask();
        await Assert.That(replacement.IsCompleted).IsFalse();

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(await replacement.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(2);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public Task Cancelling_Head_Middle_Or_Tail_Compacts_The_Queue(int cancelledIndex) =>
        AssertCancellationCompactsQueue(cancelledIndex);

    [Test]
    public async Task Cancellation_Before_Permit_Wins_Without_Invoking_The_Delegate()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = CreateShield(fakeTime, queueLimit: 1);
        using var cancellation = new CancellationTokenSource();
        var invoked = false;

        await shield.ExecuteAsync(_ => new ValueTask<int>(0));
        var queued = shield.ExecuteAsync(_ =>
        {
            invoked = true;
            return new ValueTask<int>(1);
        }, cancellation.Token).AsTask();

        cancellation.Cancel();
        fakeTime.Advance(TimeSpan.FromSeconds(1));

        await Assert.That(async () => await queued).Throws<OperationCanceledException>();
        await Assert.That(invoked).IsFalse();
        await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(2))).IsEqualTo(2);
    }

    [Test]
    public async Task Permit_Before_Cancellation_Consumes_Exactly_One_Permit()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = CreateShield(fakeTime, queueLimit: 1);
        using var cancellation = new CancellationTokenSource();

        await shield.ExecuteAsync(_ => new ValueTask<int>(0));
        var queued = shield.ExecuteAsync(_ => new ValueTask<int>(1), cancellation.Token).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(await queued.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(1);
        cancellation.Cancel();

        var replacement = shield.ExecuteAsync(_ => new ValueTask<int>(2)).AsTask();
        await Assert.That(replacement.IsCompleted).IsFalse();
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(await replacement.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(2);
    }

    [Test]
    public async Task Concurrent_Callers_Admit_Only_Burst_And_Queue_Capacity()
    {
        const int burst = 2;
        const int queueLimit = 3;
        const int callerCount = 12;
        var fakeTime = new FakeTimeProvider();
        var shield = Shield
            .RateLimit(options =>
            {
                options.Permits = 1;
                options.Window = TimeSpan.FromSeconds(1);
                options.Burst = burst;
                options.QueueLimit = queueLimit;
            })
            .WithTimeProvider(fakeTime);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = Enumerable.Range(0, callerCount).Select(async index =>
        {
            await start.Task;
            return shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(index)).AsTask();
        }).ToArray();

        start.SetResult();
        var calls = await Task.WhenAll(workers);

        var completed = calls.Where(call => call.IsCompleted).Select(call => call.Result).ToArray();
        var rejections = completed
            .Select(outcome => outcome.Exception)
            .OfType<RateLimitExceededException>()
            .ToArray();
        await Assert.That(completed.Count(outcome => outcome.IsSuccess)).IsEqualTo(burst);
        await Assert.That(rejections.Length).IsEqualTo(callerCount - burst - queueLimit);
        await Assert.That(rejections.All(rejection => rejection.RetryAfter is { } retryAfter
            && retryAfter > TimeSpan.Zero
            && retryAfter < TimeSpan.MaxValue)).IsTrue();
        await Assert.That(calls.Count(call => !call.IsCompleted)).IsEqualTo(queueLimit);

        for (var permit = 1; permit <= queueLimit; permit++)
        {
            fakeTime.Advance(TimeSpan.FromSeconds(1));
            fakeTime.Advance(TimeSpan.FromTicks(1));
            var expectedCompleted = callerCount - queueLimit + permit;
            if (calls.Count(call => call.IsCompleted) < expectedCompleted)
            {
                var pending = calls.Where(call => !call.IsCompleted).ToArray();
                if (pending.Length > 0)
                {
                    var next = await WaitWithMessage(
                        Task.WhenAny(pending),
                        $"Permit {permit} stalled with {calls.Count(call => call.IsCompleted)} completed calls.");
                    await next;
                }
            }

            await Assert.That(calls.Count(call => call.IsCompleted))
                .IsEqualTo(expectedCompleted);
        }

        await Assert.That((await Task.WhenAll(calls)).Count(outcome => outcome.IsSuccess))
            .IsEqualTo(burst + queueLimit);
    }

    [Test]
    public async Task Repeated_Cancel_And_Requeue_Leaves_No_Phantom_Debt()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = CreateShield(fakeTime, queueLimit: 1);

        await shield.ExecuteAsync(_ => new ValueTask<int>(0));

        for (var cycle = 0; cycle < 50; cycle++)
        {
            using var cancellation = new CancellationTokenSource();
            var queued = shield.ExecuteAsync(_ => new ValueTask<int>(cycle), cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.That(async () => await queued).Throws<OperationCanceledException>();
        }

        var final = shield.ExecuteAsync(_ => new ValueTask<int>(51)).AsTask();
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(await final.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(51);
    }

    private static Shield CreateShield(FakeTimeProvider fakeTime, int queueLimit) => Shield
        .RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromSeconds(1);
            options.Burst = 1;
            options.QueueLimit = queueLimit;
        })
        .WithTimeProvider(fakeTime);

    private static async Task<T> WaitWithMessage<T>(Task<T> task, string message)
    {
        try
        {
            return await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(message, exception);
        }
    }

    private static async Task AssertCancellationCompactsQueue(int cancelledIndex)
    {
        var fakeTime = new FakeTimeProvider();
        var shield = CreateShield(fakeTime, queueLimit: 3);
        var cancellations = Enumerable.Range(0, 3).Select(_ => new CancellationTokenSource()).ToArray();

        try
        {
            await shield.ExecuteAsync(_ => new ValueTask<int>(-1));
            var queued = Enumerable.Range(0, 3)
                .Select(index => shield.ExecuteAsync(_ => new ValueTask<int>(index), cancellations[index].Token).AsTask())
                .ToArray();

            cancellations[cancelledIndex].Cancel();
            var cancellation = await Assert.That(async () => await queued[cancelledIndex])
                .Throws<OperationCanceledException>();
            await Assert.That(cancellation!.CancellationToken).IsEqualTo(cancellations[cancelledIndex].Token);

            var replacement = shield.ExecuteAsync(_ => new ValueTask<int>(3)).AsTask();
            var active = queued.Where((_, index) => index != cancelledIndex).Append(replacement).ToArray();
            for (var permit = 0; permit < active.Length; permit++)
            {
                fakeTime.Advance(TimeSpan.FromSeconds(1));
                fakeTime.Advance(TimeSpan.FromTicks(1));
                await WaitWithMessage(
                    active[permit],
                    $"Cancellation index {cancelledIndex}, permit {permit + 1} stalled.");
                await Assert.That(active.Take(permit + 1).All(task => task.IsCompletedSuccessfully)).IsTrue();
                await Assert.That(active.Skip(permit + 1).All(task => !task.IsCompleted)).IsTrue();
            }
        }
        finally
        {
            foreach (var cancellation in cancellations)
            {
                cancellation.Dispose();
            }
        }
    }
}
