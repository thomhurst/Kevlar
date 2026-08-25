using System.Net;
using Kevlar.Extensions.Http;

namespace Kevlar.NetStandard.Tests;

public class HttpPropertiesReplayTests
{
    [Test]
    public async Task Transient_Classifier_Handles_NetStandard_Timeout_Shape()
    {
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();

        await Assert.That(HttpShield.IsTransientException(
            new TaskCanceledException(),
            CancellationToken.None)).IsTrue();
        await Assert.That(HttpShield.IsTransientException(
            new TaskCanceledException("HttpClient timeout", new TimeoutException()),
            CancellationToken.None)).IsTrue();
        await Assert.That(HttpShield.IsTransientException(
            new TaskCanceledException("caller", null, callerCancellation.Token),
            callerCancellation.Token)).IsFalse();
    }

    [Test]
    public async Task NonReplayable_Post_Returns_Original_Response()
    {
        var attempts = 0;
        using var invoker = new HttpMessageInvoker(new ShieldDelegatingHandler(
            HttpShield.WhenTransient().Retry(1, Backoff.None))
        {
            InnerHandler = new DelegateHandler((_, _) =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }),
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/upload")
        {
            Content = new StringContent("payload"),
        };

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task ReReadable_Put_Content_Retries_Without_Buffering()
    {
        var attempts = 0;
        using var invoker = new HttpMessageInvoker(new ShieldDelegatingHandler(
            HttpShield.WhenTransient().Retry(1, Backoff.None))
        {
            InnerHandler = new DelegateHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(
                    ++attempts == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK))),
        });
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://example.test/upload")
        {
            Content = new ByteArrayContent([1, 2, 3]),
        };

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(attempts).IsEqualTo(2);
    }

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

    [Test]
    public async Task Kevlar_Request_Options_Use_Properties_And_Flow_To_Replay()
    {
        var observed = new List<KevlarRequestOptions?>();
        using var invoker = new HttpMessageInvoker(new ShieldDelegatingHandler(
            HttpShield.WhenTransient().Retry(1, Backoff.None))
        {
            InnerHandler = new DelegateHandler((request, _) =>
            {
                observed.Add(KevlarHttp.GetRequestOptions(request));
                return Task.FromResult(new HttpResponseMessage(
                    observed.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
            }),
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/replay")
            .WithShieldName("netstandard");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

#pragma warning disable CS0618 // netstandard2.0 compatibility contract uses HttpRequestMessage.Properties.
        await Assert.That(request.Properties.ContainsKey("Kevlar.RequestOptions")).IsTrue();
#pragma warning restore CS0618
        await Assert.That(observed.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(observed[0], observed[1])).IsTrue();
        await Assert.That(observed[1]!.ShieldName).IsEqualTo("netstandard");
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

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
