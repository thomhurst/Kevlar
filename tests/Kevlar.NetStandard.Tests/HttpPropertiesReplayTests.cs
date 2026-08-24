using System.Net;
using Kevlar.Extensions.Http;

namespace Kevlar.NetStandard.Tests;

public class HttpPropertiesReplayTests
{
    [Test]
    public async Task Buffered_Retry_Preserves_NetStandard_Request_Properties()
    {
        var marker = new object();
        var observed = new List<object?>();
        using var invoker = new HttpMessageInvoker(new ShieldDelegatingHandler(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            new ShieldHttpHandlerOptions
            {
                AllowUnsafeMethodReplay = true,
                ContentReplayPolicy = HttpContentReplayPolicy.Buffer,
            })
        {
            InnerHandler = new PropertyRecordingHandler(observed),
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/upload")
        {
            Content = new StringContent("payload"),
        };
#pragma warning disable CS0618 // netstandard2.0 compatibility contract uses HttpRequestMessage.Properties.
        request.Properties["request-marker"] = marker;
#pragma warning restore CS0618

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(observed.Count).IsEqualTo(2);
        await Assert.That(observed.All(value => ReferenceEquals(value, marker))).IsTrue();
    }

    private sealed class PropertyRecordingHandler(List<object?> observed) : HttpMessageHandler
    {
        private int _attempt;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
#pragma warning disable CS0618 // netstandard2.0 compatibility contract uses HttpRequestMessage.Properties.
            request.Properties.TryGetValue("request-marker", out var marker);
#pragma warning restore CS0618
            observed.Add(marker);
            var status = Interlocked.Increment(ref _attempt) == 1
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
