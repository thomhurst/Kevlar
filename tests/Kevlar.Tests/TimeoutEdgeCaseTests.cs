using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class TimeoutEdgeCaseTests
{
    [Test]
    public async Task A_Result_Produced_After_The_Timer_Fired_Is_Still_Delivered()
    {
        var timeProvider = new ControlledTimeProvider();
        var action = new GatedDelegate<int>("successful timeout-ignoring action", static (_, _) => new ValueTask<int>(7));
        var timedOut = false;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromMinutes(1);
            options.OnTimeout = _ => timedOut = true;
        }).WithTimeProvider(timeProvider);

        // The delegate ignores its token and completes anyway; the timeout is cooperative,
        // so the successful result wins and no timeout is reported.
        var task = shield.ExecuteAsync(action.InvokeAsync).AsTask();
        await action.WaitForInvocationsAsync(1);
        timeProvider.FireTimer(0);
        action.Release();
        var result = await task;

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(timedOut).IsFalse();
    }

    [Test]
    public async Task A_NonCancellation_Failure_After_The_Timer_Fired_Is_Not_Rewritten()
    {
        var timeProvider = new ControlledTimeProvider();
        var action = new GatedDelegate<int>("failing timeout-ignoring action", static (_, _) =>
            throw new InvalidOperationException("real failure"));
        var shield = Shield.Timeout(TimeSpan.FromMinutes(1)).WithTimeProvider(timeProvider);

        var task = shield.ExecuteAsync(action.InvokeAsync).AsTask();
        await action.WaitForInvocationsAsync(1);
        timeProvider.FireTimer(0);
        action.Release();

        await Assert.That(async () => await task).Throws<InvalidOperationException>().WithMessage("real failure");
    }

    [Test]
    public async Task Nested_Timeouts_The_Outer_One_Fires_First()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield
            .Timeout(TimeSpan.FromSeconds(1))
            .Timeout(TimeSpan.FromHours(1))
            .WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync(async token =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(1));

        var exception = await Assert.That(async () => await task).Throws<TimeoutExceededException>();
        await Assert.That(exception!.Timeout).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task Nested_Timeouts_The_Inner_One_Fires_First()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield
            .Timeout(TimeSpan.FromHours(1))
            .Timeout(TimeSpan.FromSeconds(1))
            .WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync(async token =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(1));

        var exception = await Assert.That(async () => await task).Throws<TimeoutExceededException>();
        await Assert.That(exception!.Timeout).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task Caller_Cancellation_Does_Not_Invoke_OnTimeout()
    {
        using var cancellation = new CancellationTokenSource();
        var timedOut = false;
        var probeReady = new TaskCompletionSource<CancellationProbe>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromMinutes(10);
            options.OnTimeout = _ => timedOut = true;
        });

        var task = shield.ExecuteAsync(async token =>
        {
            using var probe = new CancellationProbe(token);
            probeReady.SetResult(probe);
            await probe.WaitAsync();
            token.ThrowIfCancellationRequested();
            return 1;
        }, cancellation.Token).AsTask();

        var cancellationProbe = await TestHelpers.WaitAsync(probeReady.Task, "timeout action to register cancellation");
        cancellation.Cancel();
        await cancellationProbe.WaitAsync();

        await Assert.That(async () => await task).Throws<OperationCanceledException>();
        await Assert.That(timedOut).IsFalse();
        await Assert.That(cancellationProbe.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Each_Retry_Attempt_Gets_A_Fresh_Timeout()
    {
        var fakeTime = new FakeTimeProvider();
        var attempts = 0;
        var attemptsStarted = new AsyncCounter("per-attempt timeouts");

        // Retry is outermost, so the inner timeout budget restarts per attempt.
        var shield = Shield
            .Retry(2, Backoff.None)
            .Timeout(TimeSpan.FromSeconds(1))
            .WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync(async token =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            attemptsStarted.Signal();
            if (attempt < 3)
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            }

            return attempt;
        }).AsTask();

        await attemptsStarted.WaitForAsync(1);
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await attemptsStarted.WaitForAsync(2);
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await attemptsStarted.WaitForAsync(3);

        var result = await task;
        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task The_Exception_Reports_The_Configured_Timeout()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield.Timeout(TimeSpan.FromSeconds(2.5)).WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync(async token =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(2.5));

        var exception = await Assert.That(async () => await task).Throws<TimeoutExceededException>();
        await Assert.That(exception!.Timeout).IsEqualTo(TimeSpan.FromSeconds(2.5));
    }

    [Test]
    public async Task OnTimeout_Receives_The_Context()
    {
        var fakeTime = new FakeTimeProvider();
        string? seenShieldName = null;
        var shield = Shield
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeout = timeout => seenShieldName = timeout.Context.ShieldName;
            })
            .WithName("timeout-shield")
            .WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync(async token =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(1));

        await Assert.That(async () => await task).Throws<TimeoutExceededException>();
        await Assert.That(seenShieldName).IsEqualTo("timeout-shield");
    }

    [Test]
    public async Task A_Queued_Custom_Timer_Callback_Cannot_Cancel_A_Later_Execution()
    {
        var timeProvider = new ControlledTimeProvider();
        ControlledTimeProvider.QueuedTimerCallback? queuedCallback = null;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield
            .Timeout(TimeSpan.FromMinutes(1))
            .WithTimeProvider(timeProvider);

        await shield.ExecuteAsync(_ =>
        {
            queuedCallback = timeProvider.QueueTimerCallback(0);
            return new ValueTask<int>(1);
        });

        await Assert.That(timeProvider.IsTimerDisposed(0)).IsTrue();
        await Assert.That(timeProvider.QueuedCallbackCount).IsEqualTo(1);

        var task = shield.ExecuteAsync(async token =>
        {
            await release.Task;
            token.ThrowIfCancellationRequested();
            return 2;
        }).AsTask();

        queuedCallback!.Fire();
        release.SetResult();

        await Assert.That(await task).IsEqualTo(2);
        await Assert.That(timeProvider.QueuedCallbackCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_Setup_Failure_Disposes_The_Linked_Source()
    {
        var timeProvider = new ThrowingTimeProvider();
        var shield = Shield
            .Timeout(TimeSpan.FromMinutes(1))
            .WithTimeProvider(timeProvider);

        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => timeProvider.Source!.Token).Throws<ObjectDisposedException>();
    }

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public CancellationTokenSource? Source { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Source = (CancellationTokenSource)state!;
            throw new InvalidOperationException("Timer setup failed.");
        }
    }
}
