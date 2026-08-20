using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class RateLimitTests
{
    [Test]
    public async Task Allows_Burst_Then_Rejects()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = Policy.RateLimit(2, TimeSpan.FromSeconds(10)).WithTimeProvider(fakeTime);

        await policy.ExecuteAsync(_ => new ValueTask<int>(1));
        await policy.ExecuteAsync(_ => new ValueTask<int>(2));

        var exception = await Assert.That(async () => await policy.ExecuteAsync(_ => new ValueTask<int>(3)))
            .Throws<RateLimitExceededException>();

        await Assert.That(exception!.RetryAfter!.Value > TimeSpan.Zero).IsTrue();
    }

    [Test]
    public async Task Permits_Refill_Over_Time()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = Policy.RateLimit(2, TimeSpan.FromSeconds(10)).WithTimeProvider(fakeTime);

        await policy.ExecuteAsync(_ => new ValueTask<int>(1));
        await policy.ExecuteAsync(_ => new ValueTask<int>(2));

        fakeTime.Advance(TimeSpan.FromSeconds(5));

        var result = await policy.ExecuteAsync(_ => new ValueTask<int>(3));
        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task Queued_Executions_Wait_For_A_Permit()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = Policy
            .RateLimit(options =>
            {
                options.Permits = 1;
                options.Window = TimeSpan.FromSeconds(10);
                options.QueueLimit = 1;
            })
            .WithTimeProvider(fakeTime);

        await policy.ExecuteAsync(_ => new ValueTask<int>(1));

        var queued = policy.ExecuteAsync(_ => new ValueTask<int>(2)).AsTask();
        await Assert.That(queued.IsCompleted).IsFalse();

        // The queue is now full, so a third execution is rejected immediately.
        await Assert.That(async () => await policy.ExecuteAsync(_ => new ValueTask<int>(3)))
            .Throws<RateLimitExceededException>();

        fakeTime.Advance(TimeSpan.FromSeconds(10));

        var result = await queued;
        await Assert.That(result).IsEqualTo(2);
    }
}
