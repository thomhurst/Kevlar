using Kevlar.Internal;

namespace Kevlar.Tests;

[NotInParallel]
public class TimeoutPoolTests
{
    [Test]
    public async Task Concurrent_Expiry_And_Completion_Do_Not_Cancel_Later_Rentals()
    {
        var shortTimeout = Shield.Timeout(TimeSpan.FromMilliseconds(1));
        var longTimeout = Shield.Timeout(TimeSpan.FromSeconds(30));
        var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var iteration = 0; iteration < 50; iteration++)
            {
                var outcome = await shortTimeout.ExecuteOutcomeAsync<int>(async token =>
                {
                    await Task.Delay(iteration % 3, token);
                    return 42;
                });
                if (!outcome.IsSuccess)
                {
                    await Assert.That(outcome.Exception).IsTypeOf<TimeoutExceededException>();
                }
                await longTimeout.ExecuteAsync(async token =>
                {
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                });
            }
        }));
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Concurrent_Rentals_Isolate_Cancellation(bool yield)
    {
        var shield = Shield.Timeout(TimeSpan.FromSeconds(30));
        var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                using var previous = new CancellationTokenSource();
                await shield.ExecuteAsync(static token =>
                {
                    token.ThrowIfCancellationRequested();
                    return ValueTask.CompletedTask;
                }, previous.Token);

                using var current = new CancellationTokenSource();
                var cancelCurrent = iteration % 2 == 0;
                var result = await shield.ExecuteOutcomeAsync<int>(async token =>
                {
                    if (yield)
                    {
                        await Task.Yield();
                    }
                    previous.Cancel();
                    token.ThrowIfCancellationRequested();
                    if (cancelCurrent)
                    {
                        current.Cancel();
                        token.ThrowIfCancellationRequested();
                    }
                    return 42;
                }, current.Token);

                if (cancelCurrent)
                {
                    await Assert.That(result.Exception).IsTypeOf<OperationCanceledException>();
                    await Assert.That(((OperationCanceledException)result.Exception!).CancellationToken)
                        .IsEqualTo(current.Token);
                }
                else
                {
                    await Assert.That(result.IsSuccess).IsTrue();
                    await Assert.That(result.Result).IsEqualTo(42);
                }
            }
        }));
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Reuse_Uses_Current_Deadline_And_Removes_Previous_Registrations(bool shorten)
    {
        using var previous = new CancellationTokenSource();
        var source = TimeoutSourcePool.RentLinked(previous.Token);
        var staleCallbacks = 0;
        using var oldRegistration = source.Token.Register(() => Interlocked.Increment(ref staleCallbacks));
        TimeoutSourcePool.Arm(source, shorten ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(50));
        source.Dispose();
        using var next = TimeoutSourcePool.RentLinked(CancellationToken.None);
        var reused = ReferenceEquals(source, next);
        TimeoutSourcePool.Arm(next, shorten ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromSeconds(30));
        previous.Cancel();
        await Assert.That(reused).IsTrue();
        if (shorten)
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = next.Token.Register(() => cancelled.TrySetResult());
            await cancelled.Task.WaitAsync(TestHelpers.DefaultTimeout);
        }
        else
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150));
            await Assert.That(next.IsCancellationRequested).IsFalse();
        }
        await Assert.That(staleCallbacks).IsEqualTo(0);
    }

    [Test]
    public async Task Completion_Does_Not_Wait_For_Timeout_Callback_And_Does_Not_Reuse_Its_Source()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var source = TimeoutSourcePool.RentLinked(CancellationToken.None);
        using var registration = source.Token.UnsafeRegister(_ =>
        {
            entered.Set();
            release.Wait();
        }, null);
        TimeoutSourcePool.Arm(source, TimeSpan.FromMilliseconds(20));
        Task? returning = null;
        try
        {
            await Assert.That(entered.Wait(TestHelpers.DefaultTimeout)).IsTrue();
            returning = Task.Factory.StartNew(source.Dispose, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await returning.WaitAsync(TestHelpers.DefaultTimeout);
            using var next = TimeoutSourcePool.RentLinked(CancellationToken.None);
            await Assert.That(ReferenceEquals(source, next)).IsFalse();
            await Assert.That(next.IsCancellationRequested).IsFalse();
        }
        finally
        {
            release.Set();
            if (returning is not null)
            {
                await returning.WaitAsync(TestHelpers.DefaultTimeout);
            }
            else
            {
                source.Dispose();
            }
        }
    }

    [Test]
    public async Task Return_Drains_Upstream_Cancellation_Before_Reuse()
    {
        using var upstream = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var source = TimeoutSourcePool.RentLinked(upstream.Token);
        using var registration = source.Token.Register(() =>
        {
            entered.Set();
            release.Wait();
        });
        var cancellation = Task.Factory.StartNew(upstream.Cancel, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task? returning = null;
        try
        {
            await Assert.That(entered.Wait(TestHelpers.DefaultTimeout)).IsTrue();
            var returningStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            returning = Task.Factory.StartNew(() =>
            {
                returningStarted.TrySetResult();
                source.Dispose();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await returningStarted.Task.WaitAsync(TestHelpers.DefaultTimeout);
            await Assert.That(returning.IsCompleted).IsFalse();
        }
        finally
        {
            release.Set();
            await cancellation.WaitAsync(TestHelpers.DefaultTimeout);
            if (returning is not null)
            {
                await returning.WaitAsync(TestHelpers.DefaultTimeout);
            }
            else
            {
                source.Dispose();
            }
        }
        using var next = TimeoutSourcePool.RentLinked(CancellationToken.None);
        await Assert.That(next.IsCancellationRequested).IsFalse();
    }

    [Test]
    public async Task Timer_Does_Not_Flow_A_Creating_Callers_ExecutionContext()
    {
        var local = new AsyncLocal<string?>();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = Task.Factory.StartNew(() =>
        {
            local.Value = "private caller context";
            using var source = TimeoutSourcePool.RentLinked(CancellationToken.None);
            using var registration = source.Token.UnsafeRegister(_ => completion.TrySetResult(local.Value), null);
            TimeoutSourcePool.Arm(source, TimeSpan.FromMilliseconds(20));
            _ = completion.Task.Wait(TestHelpers.DefaultTimeout);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await Assert.That(await completion.Task.WaitAsync(TestHelpers.DefaultTimeout)).IsNull();
        await worker.WaitAsync(TestHelpers.DefaultTimeout);
    }
}
