using Kevlar.Extensions.Http;
using System.Net;

namespace Kevlar.NetStandard.Tests;

public class HttpBufferCancellationTests
{
    [Test]
    public async Task NetStandard_Buffering_Timeout_Does_Not_Wait_For_Blocking_Serializer()
    {
        var content = new BlockingContent();
        using var invoker = CreateInvoker(
            Shield.Timeout(TimeSpan.FromMilliseconds(50)).For<HttpResponseMessage>());
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://example.test/upload")
        {
            Content = content,
        };

        var send = invoker.SendAsync(request, CancellationToken.None);
        await content.Started.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            _ = await Assert.That(async () => await send.WaitAsync(TimeSpan.FromSeconds(5)))
                .Throws<TimeoutExceededException>();
        }
        finally
        {
            content.Release();
        }
    }

    [Test]
    public async Task NetStandard_Buffering_Caller_Cancellation_Preserves_Caller_Token()
    {
        using var cancellation = new CancellationTokenSource();
        var content = new BlockingContent();
        using var invoker = CreateInvoker(Shield<HttpResponseMessage>.Empty);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://example.test/upload")
        {
            Content = content,
        };
        var send = invoker.SendAsync(request, cancellation.Token);
        await content.Started.WaitAsync(TimeSpan.FromSeconds(5));

        OperationCanceledException? exception;
        try
        {
            cancellation.Cancel();
            exception = await Assert.That(async () => await send.WaitAsync(TimeSpan.FromSeconds(5)))
                .Throws<OperationCanceledException>();
        }
        finally
        {
            content.Release();
        }

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task NetStandard_Buffering_Timeout_Retry_Reuses_InFlight_Buffering()
    {
        var content = new BlockingContent();
        var shield = Shield.For<HttpResponseMessage>()
            .When<TimeoutExceededException>()
            .Retry(1, Backoff.None)
            .Timeout(TimeSpan.FromMilliseconds(50));
        using var invoker = CreateInvoker(shield);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://example.test/upload")
        {
            Content = content,
        };
        var send = invoker.SendAsync(request, CancellationToken.None);
        await content.Started.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            _ = await Assert.That(async () => await send.WaitAsync(TimeSpan.FromSeconds(5)))
                .Throws<TimeoutExceededException>();
            await Assert.That(content.SerializationAttempts).IsEqualTo(1);
        }
        finally
        {
            content.Release();
        }
    }

    private static HttpMessageInvoker CreateInvoker(Shield<HttpResponseMessage> shield) => new(
        new ShieldDelegatingHandler(
            shield,
            new ShieldHttpHandlerOptions
            {
                ContentReplayPolicy = HttpContentReplayPolicy.Buffer,
            })
        {
            InnerHandler = new TerminalHandler(),
        });

    private sealed class BlockingContent : HttpContent
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _serializationAttempts;

        public Task Started => _started.Task;

        public int SerializationAttempts => Volatile.Read(ref _serializationAttempts);

        public void Release() => _release.TrySetResult();

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Interlocked.Increment(ref _serializationAttempts);
            _started.TrySetResult();
            return _release.Task;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class TerminalHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
