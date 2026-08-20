using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class TimeoutTests
{
    [Test]
    public async Task Exceeding_The_Timeout_Throws_TimeoutExceeded()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = Policy.Timeout(TimeSpan.FromSeconds(5)).WithTimeProvider(fakeTime);

        var task = policy.ExecuteAsync(async token =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(5));

        var exception = await Assert.That(async () => await task).Throws<TimeoutExceededException>();
        await Assert.That(exception!.Timeout).IsEqualTo(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Fast_Executions_Pass_Through()
    {
        var policy = Policy.Timeout(TimeSpan.FromSeconds(30));

        var result = await policy.ExecuteAsync(_ => new ValueTask<string>("done"));

        await Assert.That(result).IsEqualTo("done");
    }

    [Test]
    public async Task Caller_Cancellation_Is_Not_Reported_As_Timeout()
    {
        var fakeTime = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var policy = Policy.Timeout(TimeSpan.FromMinutes(5)).WithTimeProvider(fakeTime);

        var task = policy.ExecuteAsync(async token =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.That(async () => await task).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task OnTimeout_Fires()
    {
        var fakeTime = new FakeTimeProvider();
        var observed = TimeSpan.Zero;
        var policy = Policy
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(2);
                options.OnTimeout = timeout => observed = timeout.Timeout;
            })
            .WithTimeProvider(fakeTime);

        var task = policy.ExecuteAsync(async token =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(2));

        await Assert.That(async () => await task).Throws<TimeoutExceededException>();
        await Assert.That(observed).IsEqualTo(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Retry_Can_Handle_Attempt_Timeouts()
    {
        var fakeTime = new FakeTimeProvider();
        var attempts = 0;
        var policy = Policy
            .Handle<TimeoutExceededException>()
            .Retry(1, Backoff.None)
            .Timeout(TimeSpan.FromSeconds(1))
            .WithTimeProvider(fakeTime);

        var task = policy.ExecuteAsync(async token =>
        {
            Interlocked.Increment(ref attempts);
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await TestHelpers.WaitUntil(() => Volatile.Read(ref attempts) == 2);
        fakeTime.Advance(TimeSpan.FromSeconds(1));

        await Assert.That(async () => await task).Throws<TimeoutExceededException>();
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Sync_Execution_Times_Out_When_Token_Is_Observed()
    {
        var policy = Policy.Timeout(TimeSpan.FromMilliseconds(100));

        await Assert.That(() => policy.Execute(token =>
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                Thread.Sleep(5);
            }

#pragma warning disable CS0162
            return 1;
#pragma warning restore CS0162
        })).Throws<TimeoutExceededException>();
    }
}
