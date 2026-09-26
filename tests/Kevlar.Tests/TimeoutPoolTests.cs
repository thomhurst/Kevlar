using Kevlar.Internal;

namespace Kevlar.Tests;

public class TimeoutPoolTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Concurrent_Rentals_Isolate_Cancellation_Across_Reused_Sources(bool yield)
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
    public async Task Reuse_Removes_Previous_Callers_And_Their_Token_Registrations()
    {
        using var previous = new CancellationTokenSource();
        var source = TimeoutSourcePool.RentLinked(previous.Token);
        var staleCallbackCount = 0;
        using var registration = source.Token.Register(() => Interlocked.Increment(ref staleCallbackCount));
        source.Dispose();
        using var next = TimeoutSourcePool.RentLinked(CancellationToken.None);
        // Force actual reuse so the assertions exercise reset rather than a fresh source.
        var reused = ReferenceEquals(source, next);
        previous.Cancel();
        var cancelledByPrevious = next.IsCancellationRequested;
        next.Cancel();
        await Assert.That(reused).IsTrue();
        await Assert.That(cancelledByPrevious).IsFalse();
        await Assert.That(staleCallbackCount).IsEqualTo(0);
    }

    [Test]
    public async Task Return_Waits_For_In_Flight_Upstream_Cancellation()
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
            await Assert.That(entered.Wait(TimeSpan.FromSeconds(5))).IsTrue();
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
}
