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

        _ = await Assert.That(async () => await send.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<TimeoutExceededException>();
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

        cancellation.Cancel();
        var exception = await Assert.That(async () => await send.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<OperationCanceledException>();

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
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
        private readonly TaskCompletionSource _never = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            _started.TrySetResult();
            return _never.Task;
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
