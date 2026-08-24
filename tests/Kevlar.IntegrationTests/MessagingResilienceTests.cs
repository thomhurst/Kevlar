using System.Collections.Concurrent;
using System.Diagnostics;
using Kevlar.IntegrationTests.Infrastructure;

namespace Kevlar.IntegrationTests;

/// <summary>
/// Async messaging scenarios over a Channels-backed broker: flaky publishes, rate-limited
/// bursts, bounded-concurrency consumers, and poison-message dead-lettering.
/// </summary>
public class MessagingResilienceTests
{
    [Test]
    public async Task Publisher_Retries_Through_A_Broker_Outage()
    {
        var broker = new InMemoryBroker();
        broker.FailNextPublishes(2);

        var publisher = Shield
            .When<BrokerUnavailableException>()
            .Retry(5, Backoff.Constant(TimeSpan.FromMilliseconds(10)));

        await publisher.ExecuteAsync(ct => broker.PublishAsync(new BrokerMessage("m1", "payload"), ct));

        await Assert.That(broker.PublishedCount).IsEqualTo(1);
        await Assert.That(broker.PublishAttempts).IsEqualTo(3);
    }

    [Test]
    public async Task Rate_Limited_Publisher_Smooths_A_Burst()
    {
        var broker = new InMemoryBroker();

        // 10 permits per 200ms with room to queue the rest: a burst of 20 all succeed,
        // but the second half is paced instead of hammering the broker.
        var publisher = Shield.RateLimit(options =>
        {
            options.Permits = 10;
            options.Window = TimeSpan.FromMilliseconds(200);
            options.QueueLimit = 10;
        });

        var stopwatch = Stopwatch.StartNew();
        var publishes = Enumerable.Range(1, 20)
            .Select(i => publisher.ExecuteAsync(ct => broker.PublishAsync(new BrokerMessage($"m{i}", "payload"), ct)).AsTask());
        await Task.WhenAll(publishes);
        stopwatch.Stop();

        await Assert.That(broker.PublishedCount).IsEqualTo(20);
        await Assert.That(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(120)).IsTrue();
    }

    [Test]
    public async Task Consumer_Retries_Transients_And_DeadLetters_Poison_Messages()
    {
        var broker = new InMemoryBroker();

        foreach (var id in new[] { "a", "b", "flaky", "poison", "c" })
        {
            await broker.PublishAsync(new BrokerMessage(id, id == "poison" ? "poison" : "ok"), CancellationToken.None);
        }

        var attempts = new ConcurrentDictionary<string, int>();
        var processed = new ConcurrentBag<string>();
        var deadLetters = new ConcurrentBag<string>();

        // Real-world consumer mix: fallback (dead-letter) outermost, then bounded retries.
        var consumer = Shield.For<bool>()
            .When<MessageProcessingException>()
            .Fallback((outcome, _) =>
            {
                deadLetters.Add(((MessageProcessingException)outcome.Exception!).FailedMessage.Id);
                return new ValueTask<bool>(false);
            })
            .Retry(2, Backoff.None);

        while (broker.TryConsume(out var message))
        {
            await consumer.ExecuteAsync(_ =>
            {
                var attempt = attempts.AddOrUpdate(message.Id, 1, (_, n) => n + 1);

                if (message.Body == "poison" || (message.Id == "flaky" && attempt < 3))
                {
                    throw new MessageProcessingException(message);
                }

                processed.Add(message.Id);
                return new ValueTask<bool>(true);
            });
        }

        await Assert.That(processed.Order().ToArray()).IsEquivalentTo(["a", "b", "c", "flaky"]);
        await Assert.That(deadLetters.ToArray()).IsEquivalentTo(["poison"]);
        await Assert.That(attempts["poison"]).IsEqualTo(3);
        await Assert.That(attempts["flaky"]).IsEqualTo(3);
    }

    [Test]
    public async Task Bulkhead_Bounds_Consumer_Concurrency()
    {
        var broker = new InMemoryBroker();

        for (var i = 0; i < 20; i++)
        {
            await broker.PublishAsync(new BrokerMessage($"m{i}", "payload"), CancellationToken.None);
        }

        var current = 0;
        var maxObserved = 0;
        var handled = 0;

        var consumer = Shield.ConcurrencyLimit(maxConcurrency: 4, queueLimit: 16);

        var workers = new List<Task>();
        while (broker.TryConsume(out _))
        {
            workers.Add(consumer.ExecuteAsync(async _ =>
            {
                var now = Interlocked.Increment(ref current);
                InterlockedMax(ref maxObserved, now);
                await Task.Delay(25);
                Interlocked.Decrement(ref current);
                Interlocked.Increment(ref handled);
            }).AsTask());
        }

        await Task.WhenAll(workers);

        await Assert.That(handled).IsEqualTo(20);
        await Assert.That(maxObserved <= 4).IsTrue();
    }

    [Test]
    public async Task Full_Pipeline_Producer_To_Consumer_With_Outage_And_Poison()
    {
        var broker = new InMemoryBroker();
        broker.FailNextPublishes(3);

        var publisher = Shield
            .When<BrokerUnavailableException>()
            .Retry(5, Backoff.Constant(TimeSpan.FromMilliseconds(5)))
            .RateLimit(options =>
            {
                options.Permits = 100;
                options.Window = TimeSpan.FromSeconds(1);
                options.QueueLimit = 100;
            });

        for (var i = 1; i <= 10; i++)
        {
            var message = new BrokerMessage($"m{i}", i == 7 ? "poison" : "ok");
            await publisher.ExecuteAsync(ct => broker.PublishAsync(message, ct));
        }

        // The broker outage cost 3 extra attempts; every message still made it on.
        await Assert.That(broker.PublishedCount).IsEqualTo(10);
        await Assert.That(broker.PublishAttempts).IsEqualTo(13);

        var processed = new ConcurrentBag<string>();
        var deadLetters = new ConcurrentBag<string>();

        var consumer = Shield.For<bool>()
            .When<MessageProcessingException>()
            .Fallback((outcome, _) =>
            {
                deadLetters.Add(((MessageProcessingException)outcome.Exception!).FailedMessage.Id);
                return new ValueTask<bool>(false);
            })
            .Retry(2, Backoff.None);

        while (broker.TryConsume(out var message))
        {
            await consumer.ExecuteAsync(_ =>
            {
                if (message.Body == "poison")
                {
                    throw new MessageProcessingException(message);
                }

                processed.Add(message.Id);
                return new ValueTask<bool>(true);
            });
        }

        await Assert.That(processed.Count).IsEqualTo(9);
        await Assert.That(deadLetters.ToArray()).IsEquivalentTo(["m7"]);
    }

    [Test]
    public async Task Caller_Cancellation_During_Backoff_Stops_Publish_Retries()
    {
        var broker = new InMemoryBroker();
        broker.FailNextPublishes(100);
        var retryScheduled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = Shield
            .When<BrokerUnavailableException>()
            .Retry(options =>
            {
                options.MaxRetries = 99;
                options.Backoff = Backoff.Constant(TimeSpan.FromSeconds(30));
                options.OnRetry = _ => retryScheduled.TrySetResult();
            });
        using var cancellation = new CancellationTokenSource();

        var publish = publisher.ExecuteAsync(
            ct => broker.PublishAsync(new BrokerMessage("cancelled", "payload"), ct),
            cancellation.Token).AsTask();
        await retryScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        var exception = await Assert.That(async () => await publish)
            .Throws<OperationCanceledException>();

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(broker.PublishAttempts).IsEqualTo(1);
        await Assert.That(broker.PublishedCount).IsEqualTo(0);
        await Assert.That(broker.TryConsume(out _)).IsFalse();
    }

    [Test]
    public async Task Concurrent_Retries_Deliver_Each_Message_Exactly_Once()
    {
        const int messageCount = 20;
        const int transientFailures = 10;
        var broker = new InMemoryBroker();
        broker.FailNextPublishes(transientFailures);
        var publisher = Shield
            .When<BrokerUnavailableException>()
            .Retry(transientFailures, Backoff.None);
        var firstAttemptsReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstAttempts = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstAttemptCount = 0;

        var publishes = Enumerable.Range(0, messageCount).Select(index =>
        {
            var isFirstAttempt = true;
            return publisher.ExecuteAsync(async ct =>
            {
                if (isFirstAttempt)
                {
                    isFirstAttempt = false;
                    if (Interlocked.Increment(ref firstAttemptCount) == messageCount)
                    {
                        firstAttemptsReady.TrySetResult();
                    }

                    await releaseFirstAttempts.Task.WaitAsync(ct);
                }

                await broker.PublishAsync(new BrokerMessage($"m{index}", "payload"), ct);
            }).AsTask();
        }).ToArray();
        await firstAttemptsReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseFirstAttempts.TrySetResult();
        await Task.WhenAll(publishes);

        var delivered = new List<string>();
        while (broker.TryConsume(out var message))
        {
            delivered.Add(message.Id);
        }

        await Assert.That(broker.PublishAttempts).IsEqualTo(messageCount + transientFailures);
        await Assert.That(broker.PublishedCount).IsEqualTo(messageCount);
        await Assert.That(delivered.Order().ToArray()).IsEquivalentTo(
            Enumerable.Range(0, messageCount).Select(static index => $"m{index}").ToArray());
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)))
        {
            Interlocked.CompareExchange(ref target, value, seen);
        }
    }
}
