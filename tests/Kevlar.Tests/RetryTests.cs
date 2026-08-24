using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class RetryTests
{
    [Test]
    public async Task Retries_Until_Success()
    {
        var attempts = 0;
        var shield = Shield.Retry(3, Backoff.None);

        var result = await shield.ExecuteAsync(_ =>
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
        var shield = Shield.Retry(2, Backoff.None);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
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
        var shield = Shield.When<ArgumentException>().Retry(3, Backoff.None);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
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
        var shield = Shield.Retry(3, Backoff.None);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new OperationCanceledException();
        })).Throws<OperationCanceledException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task WhenResult_Retries_On_Bad_Result()
    {
        var attempts = 0;
        var shield = Shield.For<int>().WhenResult(value => value < 0).Retry(3, Backoff.None);

        var result = await shield.ExecuteAsync(_ =>
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
        var shield = Shield.For<int>().WhenResult(value => value < 0).Retry(3, Backoff.None);

        await Assert.That(async () => await shield.ExecuteAsync(_ =>
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
        var attemptsStarted = new AsyncCounter("backoff attempts");
        var shield = Shield.Retry(2, Backoff.Constant(TimeSpan.FromSeconds(1))).WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            attemptsStarted.Signal();
            throw new InvalidOperationException();
        }).AsTask();

        await Assert.That(attempts).IsEqualTo(1);

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await attemptsStarted.WaitForAsync(2);

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await attemptsStarted.WaitForAsync(3);

        await Assert.That(async () => await task).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Retry_Event_Numbers_Are_One_Based_Retry_Counts()
    {
        var events = new List<(int RetryNumber, TimeSpan Delay, Exception? Exception)>();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.OnRetry = retry => events.Add((retry.RetryNumber, retry.Delay, retry.Exception));
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(events.Select(retry => retry.RetryNumber).SequenceEqual([1, 2, 3])).IsTrue();
        await Assert.That(events[0].Delay).IsEqualTo(TimeSpan.Zero);
        await Assert.That(events[0].Exception).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task DelayGenerator_And_OnRetry_Receive_The_Same_Retry_Number_During_Sync_Execution()
    {
        var generated = new List<int>();
        var notified = new List<int>();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.DelayGenerator = retry =>
            {
                generated.Add(retry.RetryNumber);
                return TimeSpan.Zero;
            };
            options.OnRetry = retry => notified.Add(retry.RetryNumber);
        });

        await Assert.That(() => shield.Execute<int>(static _ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(generated.SequenceEqual([1, 2, 3])).IsTrue();
        await Assert.That(notified.SequenceEqual(generated)).IsTrue();
    }

    [Test]
    public async Task DelayGenerator_Overrides_Backoff()
    {
        var attempts = 0;
        var seenDelays = new List<TimeSpan>();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 2;
            options.Backoff = Backoff.Constant(TimeSpan.FromHours(1));
            options.DelayGenerator = _ => TimeSpan.Zero;
            options.OnRetry = retry => seenDelays.Add(retry.Delay);
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
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
        var shield = Shield.Retry(5, Backoff.Constant(TimeSpan.FromSeconds(10))).WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync<int>(_ =>
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
        var shield = Shield.Retry(3, Backoff.None);

        var result = shield.Execute(_ =>
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
