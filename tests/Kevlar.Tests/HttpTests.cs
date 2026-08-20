using System.Net;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class HttpTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses;

        public ScriptedHandler(params Func<HttpResponseMessage>[] responses) => _responses = new Queue<Func<HttpResponseMessage>>(responses);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var factory = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return Task.FromResult(factory());
        }
    }

    [Test]
    public async Task Handler_Retries_Transient_Failures()
    {
        var inner = new ScriptedHandler(
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            () => new HttpResponseMessage(HttpStatusCode.OK));

        var policy = HttpKevlar.HandleTransient().Retry(3, Backoff.None);
        using var client = new HttpClient(new KevlarDelegatingHandler(policy) { InnerHandler = inner });

        var response = await client.GetAsync("http://localhost/test");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Calls).IsEqualTo(3);
    }

    [Test]
    public async Task Non_Transient_Failures_Are_Not_Retried()
    {
        var inner = new ScriptedHandler(() => new HttpResponseMessage(HttpStatusCode.NotFound));

        var policy = HttpKevlar.HandleTransient().Retry(3, Backoff.None);
        using var client = new HttpClient(new KevlarDelegatingHandler(policy) { InnerHandler = inner });

        var response = await client.GetAsync("http://localhost/test");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(inner.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task RetryAfter_Header_Is_Honoured()
    {
        var fakeTime = new FakeTimeProvider();

        var tooMany = () =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        };

        var inner = new ScriptedHandler(tooMany, () => new HttpResponseMessage(HttpStatusCode.OK));

        var policy = HttpKevlar.HandleTransient()
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = HttpKevlar.RetryAfter;
            })
            .WithTimeProvider(fakeTime);

        using var client = new HttpClient(new KevlarDelegatingHandler(policy) { InnerHandler = inner });

        var task = client.GetAsync("http://localhost/test");

        await Assert.That(inner.Calls).IsEqualTo(1);

        fakeTime.Advance(TimeSpan.FromSeconds(7));

        var response = await task;
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task AddKevlar_Integrates_With_HttpClientFactory()
    {
        var inner = new ScriptedHandler(
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            () => new HttpResponseMessage(HttpStatusCode.OK));

        var services = new ServiceCollection();
        services.AddHttpClient("resilient")
            .ConfigurePrimaryHttpMessageHandler(() => inner)
            .AddKevlar(HttpKevlar.HandleTransient().Retry(2, Backoff.None));

        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("resilient");

        var response = await client.GetAsync("http://localhost/test");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task StandardPolicy_Handles_Success_Immediately()
    {
        var inner = new ScriptedHandler(() => new HttpResponseMessage(HttpStatusCode.OK));

        var services = new ServiceCollection();
        services.AddHttpClient("standard")
            .ConfigurePrimaryHttpMessageHandler(() => inner)
            .AddStandardKevlar();

        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("standard");

        var response = await client.GetAsync("http://localhost/test");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Calls).IsEqualTo(1);
    }
}
