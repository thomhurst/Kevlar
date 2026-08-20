using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class RetryTests
{
    [Test]
    public async Task Retries_Until_Success()
    {
        var attempts = 0;
        var policy = Policy.Retry(3, Backoff.None);

        var result = await policy.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new InvalidOperationException("boom");
            }

            return new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Exhausted_Retries_Rethrow_Last_Exception()
    {
        var attempts = 0;
        var policy = Policy.Retry(2, Backoff.None);

        await Assert.That(async () => await policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException($"attempt {attempts}");
        })).Throws<InvalidOperationException>().WithMessage("attempt 3");

        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Unhandled_Exception_Is_Not_Retried()
    {
        var attempts = 0;
        var policy = Policy.Handle<ArgumentException>().Retry(3, Backoff.None);

        await Assert.That(async () => await policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException("not handled");
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task OperationCanceled_Is_Not_Retried_By_Default()
    {
        var attempts = 0;
        var policy = Policy.Retry(3, Backoff.None);

        await Assert.That(async () => await policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new OperationCanceledException();
        })).Throws<OperationCanceledException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task HandleResult_Retries_On_Bad_Result()
    {
        var attempts = 0;
        var policy = Policy.For<int>().HandleResult(value => value < 0).Retry(3, Backoff.None);

        var result = await policy.ExecuteAsync(_ =>
        {
            attempts++;
            return new ValueTask<int>(attempts < 3 ? -1 : 7);
        });

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Result_Only_Handling_Does_Not_Retry_Exceptions()
    {
        var attempts = 0;
        var policy = Policy.For<int>().HandleResult(value => value < 0).Retry(3, Backoff.None);

        await Assert.That(async () => await policy.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Backoff_Delays_Use_The_TimeProvider()
    {
        var fakeTime = new FakeTimeProvider();
        var attempts = 0;
        var policy = Policy.Retry(2, Backoff.Constant(TimeSpan.FromSeconds(1))).WithTimeProvider(fakeTime);

        var task = policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        }).AsTask();

        await Assert.That(attempts).IsEqualTo(1);

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await TestHelpers.WaitUntil(() => Volatile.Read(ref attempts) == 2);

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await TestHelpers.WaitUntil(() => Volatile.Read(ref attempts) == 3);

        await Assert.That(async () => await task).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OnRetry_Receives_Event_Data()
    {
        var events = new List<(int Attempt, TimeSpan Delay, Exception? Exception)>();
        var policy = Policy.Retry(options =>
        {
            options.MaxRetries = 2;
            options.Backoff = Backoff.None;
            options.OnRetry = retry => events.Add((retry.Attempt, retry.Delay, retry.Exception));
        });

        await Assert.That(async () => await policy.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events[0].Attempt).IsEqualTo(1);
        await Assert.That(events[1].Attempt).IsEqualTo(2);
        await Assert.That(events[0].Delay).IsEqualTo(TimeSpan.Zero);
        await Assert.That(events[0].Exception).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task DelayGenerator_Overrides_Backoff()
    {
        var attempts = 0;
        var seenDelays = new List<TimeSpan>();
        var policy = Policy.Retry(options =>
        {
            options.MaxRetries = 2;
            options.Backoff = Backoff.Constant(TimeSpan.FromHours(1));
            options.DelayGenerator = _ => TimeSpan.Zero;
            options.OnRetry = retry => seenDelays.Add(retry.Delay);
        });

        await Assert.That(async () => await policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(3);
        await Assert.That(seenDelays).IsEquivalentTo([TimeSpan.Zero, TimeSpan.Zero]);
    }

    [Test]
    public async Task Cancellation_During_Backoff_Stops_Retrying()
    {
        var fakeTime = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var policy = Policy.Retry(5, Backoff.Constant(TimeSpan.FromSeconds(10))).WithTimeProvider(fakeTime);

        var task = policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        }, cancellation.Token).AsTask();

        await Assert.That(attempts).IsEqualTo(1);
        cancellation.Cancel();

        await Assert.That(async () => await task).Throws<OperationCanceledException>();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Sync_Execute_Retries()
    {
        var attempts = 0;
        var policy = Policy.Retry(3, Backoff.None);

        var result = policy.Execute(_ =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new InvalidOperationException();
            }

            return "ok";
        });

        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(attempts).IsEqualTo(2);
    }
}
