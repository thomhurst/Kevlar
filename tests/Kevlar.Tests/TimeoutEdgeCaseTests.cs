using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class TimeoutEdgeCaseTests
{
    [Test]
    public async Task A_Result_Produced_After_The_Timer_Fired_Is_Still_Delivered()
    {
        var timedOut = false;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(50);
            options.OnTimeout = _ => timedOut = true;
        });

        // The delegate ignores its token and completes anyway; the timeout is cooperative,
        // so the successful result wins and no timeout is reported.
        var result = await shield.ExecuteAsync(async _ =>
        {
            await Task.Delay(300);
            return 7;
        });

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(timedOut).IsFalse();
    }

    [Test]
    public async Task A_NonCancellation_Failure_After_The_Timer_Fired_Is_Not_Rewritten()
    {
        var shield = Shield.Timeout(TimeSpan.FromMilliseconds(50));

        await Assert.That(async () => await shield.ExecuteAsync<int>(async _ =>
        {
            await Task.Delay(300);
            throw new InvalidOperationException("real failure");
        })).Throws<InvalidOperationException>().WithMessage("real failure");
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
        var started = new TaskCompletionSource();
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromMinutes(10);
            options.OnTimeout = _ => timedOut = true;
        });

        var task = shield.ExecuteAsync(async token =>
        {
            started.SetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token).AsTask();

        await started.Task;
        cancellation.Cancel();

        await Assert.That(async () => await task).Throws<OperationCanceledException>();
        await Assert.That(timedOut).IsFalse();
    }

    [Test]
    public async Task Each_Retry_Attempt_Gets_A_Fresh_Timeout()
    {
        var fakeTime = new FakeTimeProvider();
        var attempts = 0;

        // Retry is outermost, so the inner timeout budget restarts per attempt.
        var shield = Shield
            .Retry(2, Backoff.None)
            .Timeout(TimeSpan.FromSeconds(1))
            .WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync(async token =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt < 3)
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            }

            return attempt;
        }).AsTask();

        await TestHelpers.WaitUntil(() => Volatile.Read(ref attempts) == 1);
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await TestHelpers.WaitUntil(() => Volatile.Read(ref attempts) == 2);
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await TestHelpers.WaitUntil(() => Volatile.Read(ref attempts) == 3);

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
        var timeProvider = new QueuedCallbackTimeProvider();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield
            .Timeout(TimeSpan.FromMinutes(1))
            .WithTimeProvider(timeProvider);

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));

        var task = shield.ExecuteAsync(async token =>
        {
            await release.Task;
            token.ThrowIfCancellationRequested();
            return 2;
        }).AsTask();

        timeProvider.Fire(timerIndex: 0);
        release.SetResult();

        await Assert.That(await task).IsEqualTo(2);
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

    private sealed class QueuedCallbackTimeProvider : TimeProvider
    {
        private readonly List<QueuedTimer> _timers = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new QueuedTimer(callback, state);
            _timers.Add(timer);
            return timer;
        }

        public void Fire(int timerIndex) => _timers[timerIndex].Fire();

        private sealed class QueuedTimer(TimerCallback callback, object? state) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => default;

            public void Fire() => callback(state);
        }
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
