using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class TimeoutEdgeCaseTests
{
    [Test]
    public async Task A_Result_Produced_After_The_Timer_Fired_Is_Still_Delivered()
    {
        var timedOut = false;
        var policy = Policy.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(50);
            options.OnTimeout = _ => timedOut = true;
        });

        // The delegate ignores its token and completes anyway; the timeout is cooperative,
        // so the successful result wins and no timeout is reported.
        var result = await policy.ExecuteAsync(async _ =>
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
        var policy = Policy.Timeout(TimeSpan.FromMilliseconds(50));

        await Assert.That(async () => await policy.ExecuteAsync<int>(async _ =>
        {
            await Task.Delay(300);
            throw new InvalidOperationException("real failure");
        })).Throws<InvalidOperationException>().WithMessage("real failure");
    }

    [Test]
    public async Task Nested_Timeouts_The_Outer_One_Fires_First()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = Policy
            .Timeout(TimeSpan.FromSeconds(1))
            .Timeout(TimeSpan.FromHours(1))
            .WithTimeProvider(fakeTime);

        var task = policy.ExecuteAsync(async token =>
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
        var policy = Policy
            .Timeout(TimeSpan.FromHours(1))
            .Timeout(TimeSpan.FromSeconds(1))
            .WithTimeProvider(fakeTime);

        var task = policy.ExecuteAsync(async token =>
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
        var policy = Policy.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromMinutes(10);
            options.OnTimeout = _ => timedOut = true;
        });

        var task = policy.ExecuteAsync(async token =>
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
        var policy = Policy
            .Retry(2, Backoff.None)
            .Timeout(TimeSpan.FromSeconds(1))
            .WithTimeProvider(fakeTime);

        var task = policy.ExecuteAsync(async token =>
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
        var policy = Policy.Timeout(TimeSpan.FromSeconds(2.5)).WithTimeProvider(fakeTime);

        var task = policy.ExecuteAsync(async token =>
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
        string? seenPolicyName = null;
        var policy = Policy
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeout = timeout => seenPolicyName = timeout.Context.PolicyName;
            })
            .WithName("timeout-policy")
            .WithTimeProvider(fakeTime);

        var task = policy.ExecuteAsync(async token =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(1));

        await Assert.That(async () => await task).Throws<TimeoutExceededException>();
        await Assert.That(seenPolicyName).IsEqualTo("timeout-policy");
    }
}
