using System.Threading.Channels;

namespace Kevlar.IntegrationTests.Infrastructure;

internal sealed record BrokerMessage(string Id, string Body);

internal sealed class BrokerUnavailableException : Exception
{
    public BrokerUnavailableException()
        : base("The message broker is unavailable.")
    {
    }
}

internal sealed class MessageProcessingException : Exception
{
    public MessageProcessingException(BrokerMessage failedMessage)
        : base($"Failed to process message '{failedMessage.Id}'.")
        => FailedMessage = failedMessage;

    public BrokerMessage FailedMessage { get; }
}

/// <summary>
/// A Channels-backed message broker with scriptable publish outages, standing in for
/// RabbitMQ / Service Bus style transports in integration tests.
/// </summary>
internal sealed class InMemoryBroker
{
    private readonly Channel<BrokerMessage> _channel = Channel.CreateUnbounded<BrokerMessage>();
    private int _publishFailuresRemaining;
    private int _publishAttempts;
    private int _publishedCount;

    /// <summary>Publish attempts, including ones that failed.</summary>
    public int PublishAttempts => Volatile.Read(ref _publishAttempts);

    /// <summary>Messages actually accepted onto the queue.</summary>
    public int PublishedCount => Volatile.Read(ref _publishedCount);

    /// <summary>Makes the next <paramref name="count"/> publish attempts fail with <see cref="BrokerUnavailableException"/>.</summary>
    public void FailNextPublishes(int count) => Volatile.Write(ref _publishFailuresRemaining, count);

    public async ValueTask PublishAsync(BrokerMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _publishAttempts);

        if (Interlocked.Decrement(ref _publishFailuresRemaining) >= 0)
        {
            throw new BrokerUnavailableException();
        }

        await _channel.Writer.WriteAsync(message, cancellationToken);
        Interlocked.Increment(ref _publishedCount);
    }

    public bool TryConsume(out BrokerMessage message) => _channel.Reader.TryRead(out message!);
}
