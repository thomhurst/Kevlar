using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class RateLimitEdgeCaseTests
{
    [Test]
    public async Task Tokens_Refill_Gradually_Not_All_At_Once()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield.RateLimit(10, TimeSpan.FromSeconds(10)).WithTimeProvider(fakeTime);

        // Drain the full burst.
        for (var i = 0; i < 10; i++)
        {
            await shield.ExecuteAsync(_ => new ValueTask<int>(i));
        }

        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(0)))
            .Throws<RateLimitExceededException>();

        // One second refills exactly one token (10 permits / 10 seconds).
        fakeTime.Advance(TimeSpan.FromSeconds(1));

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<RateLimitExceededException>();
    }

    [Test]
    public async Task Idle_Time_Cannot_Accumulate_More_Than_The_Burst()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield
            .RateLimit(options =>
            {
                options.Permits = 2;
                options.Window = TimeSpan.FromSeconds(1);
                options.Burst = 2;
            })
            .WithTimeProvider(fakeTime);

        // A long idle period must not bank more than Burst tokens.
        fakeTime.Advance(TimeSpan.FromMinutes(10));

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await shield.ExecuteAsync(_ => new ValueTask<int>(2));

        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(3)))
            .Throws<RateLimitExceededException>();
    }

    [Test]
    public async Task Burst_Can_Exceed_The_Sustained_Rate()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield
            .RateLimit(options =>
            {
                options.Permits = 1;
                options.Window = TimeSpan.FromSeconds(10);
                options.Burst = 5;
            })
            .WithTimeProvider(fakeTime);

        // The bucket starts full at Burst, so five immediate executions pass.
        for (var i = 0; i < 5; i++)
        {
            await shield.ExecuteAsync(_ => new ValueTask<int>(i));
        }

        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(9)))
            .Throws<RateLimitExceededException>();
    }

    [Test]
    public async Task Queued_Reservations_Are_Scheduled_In_Order()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield
            .RateLimit(options =>
            {
                options.Permits = 1;
                options.Window = TimeSpan.FromSeconds(1);
                options.QueueLimit = 2;
            })
            .WithTimeProvider(fakeTime);

        var immediate = await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(immediate).IsEqualTo(1);

        // Two executions reserve the next two replenished tokens.
        var second = shield.ExecuteAsync(_ => new ValueTask<int>(2)).AsTask();
        var third = shield.ExecuteAsync(_ => new ValueTask<int>(3)).AsTask();
        await Assert.That(second.IsCompleted).IsFalse();
        await Assert.That(third.IsCompleted).IsFalse();

        // The queue is exhausted; the next execution is rejected with an estimate.
        var rejection = await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(4)))
            .Throws<RateLimitExceededException>();
        await Assert.That(rejection!.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(3));

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(await second).IsEqualTo(2);
        await Assert.That(third.IsCompleted).IsFalse();

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(await third).IsEqualTo(3);
    }

    [Test]
    public async Task Cancelling_A_Queued_Execution_Surfaces_Cancellation()
    {
        var fakeTime = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var invoked = false;
        var shield = Shield
            .RateLimit(options =>
            {
                options.Permits = 1;
                options.Window = TimeSpan.FromSeconds(10);
                options.QueueLimit = 1;
            })
            .WithTimeProvider(fakeTime);

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));

        var queued = shield.ExecuteAsync(_ =>
        {
            invoked = true;
            return new ValueTask<int>(2);
        }, cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.That(async () => await queued).Throws<OperationCanceledException>();
        await Assert.That(invoked).IsFalse();
    }

    [Test]
    public async Task Exactly_The_Available_Permits_Win_Under_Concurrent_Load()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield.RateLimit(5, TimeSpan.FromSeconds(1)).WithTimeProvider(fakeTime);

        // Time is frozen, so no refill happens: exactly the 5 burst tokens may be taken,
        // no matter how the 25 concurrent executions race.
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ =>
            shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1)).AsTask()));

        var successes = outcomes.Count(outcome => outcome.IsSuccess);
        var rejections = outcomes.Count(outcome => outcome.Exception is RateLimitExceededException);

        await Assert.That(successes).IsEqualTo(5);
        await Assert.That(rejections).IsEqualTo(20);
    }

    [Test]
    public async Task Failures_Do_Not_Refund_Tokens()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield.RateLimit(2, TimeSpan.FromSeconds(10)).WithTimeProvider(fakeTime);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        // Both permits were consumed even though the executions failed.
        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<RateLimitExceededException>();
    }

    [Test]
    public async Task Rejections_Report_A_Positive_RetryAfter_Estimate()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield.RateLimit(1, TimeSpan.FromSeconds(10)).WithTimeProvider(fakeTime);

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));

        var rejection = await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<RateLimitExceededException>();

        // The bucket is empty; one token takes a full window to replenish.
        await Assert.That(rejection!.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(10));
    }
}
