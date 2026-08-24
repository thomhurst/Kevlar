using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class HedgingTests
{
    [Test]
    public async Task Handled_Failure_Launches_The_Next_Attempt_Immediately()
    {
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
        });

        var result = await shield.ExecuteAsync(_ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                throw new InvalidOperationException("first fails");
            }

            return new ValueTask<string>("second");
        });

        await Assert.That(result).IsEqualTo("second");
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task All_Attempts_Failing_Surfaces_The_Last_Failure()
    {
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 3;
            options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
        });

        await Assert.That(async () => await shield.ExecuteAsync<string>(_ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            throw new InvalidOperationException($"attempt {attempt}");
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Parallel_Mode_Returns_The_Fastest_Success()
    {
        var attempts = 0;
        var slowGate = new TaskCompletionSource();
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = TimeSpan.Zero;
        });

        var result = await shield.ExecuteAsync(async token =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                using var registration = token.Register(() => slowGate.TrySetResult());
                await slowGate.Task;
                token.ThrowIfCancellationRequested();
                return "slow";
            }

            return "fast";
        });

        await Assert.That(result).IsEqualTo("fast");
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Stagger_Delay_Launches_The_Second_Attempt_On_Schedule()
    {
        var fakeTime = new FakeTimeProvider();
        var attempts = 0;
        var hedges = new List<int>();
        var shield = Shield
            .Hedge(options =>
            {
                options.MaxAttempts = 2;
                options.Delay = TimeSpan.FromSeconds(1);
                options.OnHedge = hedge => hedges.Add(hedge.AttemptNumber);
            })
            .WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync(async token =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            }

            return attempt;
        }).AsTask();

        await Assert.That(attempts).IsEqualTo(1);

        fakeTime.Advance(TimeSpan.FromSeconds(1));

        var result = await task;
        await Assert.That(result).IsEqualTo(2);
        await Assert.That(hedges).IsEquivalentTo([2]);
    }

    [Test]
    public async Task Synchronous_Execution_Is_Not_Supported()
    {
        var shield = Shield.Hedge(2, TimeSpan.Zero);

        await Assert.That(() => shield.Execute(_ => 1)).Throws<NotSupportedException>();
    }
}
