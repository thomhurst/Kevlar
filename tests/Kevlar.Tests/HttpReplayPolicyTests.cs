using System.Net;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

public class HttpReplayPolicyTests
{
    [Test]
    [Arguments("GET", 2)]
    [Arguments("HEAD", 2)]
    [Arguments("OPTIONS", 2)]
    [Arguments("TRACE", 2)]
    [Arguments("PUT", 2)]
    [Arguments("DELETE", 2)]
    [Arguments("POST", 1)]
    [Arguments("PATCH", 1)]
    public async Task Null_Predicate_Preserves_Default_Method_Set(string method, int attempts)
    {
        using var transport = new CountingHandler();
        using var client = CreateClient(transport, new ShieldHttpHandlerOptions());
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://example.test/");

        using var response = await client.SendAsync(request);

        await Assert.That(transport.Attempts).IsEqualTo(attempts);
    }

    [Test]
    [Arguments("GET", 2)]
    [Arguments("PUT", 1)]
    [Arguments("DELETE", 1)]
    public async Task Predicate_Can_Narrow_The_Method_Set(string method, int attempts)
    {
        using var transport = new CountingHandler();
        using var client = CreateClient(transport, new ShieldHttpHandlerOptions
        {
            IsReplaySafeMethod = static method => method == HttpMethod.Get,
        });
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://example.test/");

        using var response = await client.SendAsync(request);

        await Assert.That(transport.Attempts).IsEqualTo(attempts);
    }

    [Test]
    [Arguments("request", false, 2)]
    [Arguments("handler", false, 2)]
    [Arguments("factory", false, 2)]
    [Arguments("request", true, 1)]
    [Arguments("handler", true, 1)]
    [Arguments("factory", true, 1)]
    public async Task Explicit_Replay_Overrides_Bypass_Predicate_And_Disable_Wins(
        string optIn, bool disableReplay, int attempts)
    {
        using var transport = new CountingHandler();
        using var client = CreateClient(transport, new ShieldHttpHandlerOptions
        {
            IsReplaySafeMethod = static _ => throw new InvalidOperationException("Predicate must be bypassed."),
            AllowUnsafeMethodReplay = optIn == "handler",
            RequestFactory = optIn == "factory"
                ? static (original, _, _) => new ValueTask<HttpRequestMessage>(
                    new HttpRequestMessage(original.Method, original.RequestUri))
                : null,
        });
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://example.test/");
        if (optIn == "request")
        {
            request.AllowReplay();
        }

        if (disableReplay)
        {
            request.DisableReplay();
        }

        using var response = await client.SendAsync(request);

        await Assert.That(transport.Attempts).IsEqualTo(attempts);
    }

    [Test]
    [Arguments(false, 1)]
    [Arguments(true, 2)]
    public async Task Predicate_Can_Widen_Method_Set_And_Preserves_Content_Safety(bool buffer, int attempts)
    {
        using var transport = new CountingHandler();
        using var client = CreateClient(transport, new ShieldHttpHandlerOptions
        {
            IsReplaySafeMethod = static method => method == HttpMethod.Post,
            ContentReplayPolicy = buffer ? HttpContentReplayPolicy.Buffer : HttpContentReplayPolicy.NoBuffer,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/")
        {
            Content = new StreamContent(new MemoryStream([1, 2, 3])),
        };

        using var response = await client.SendAsync(request);

        await Assert.That(transport.Attempts).IsEqualTo(attempts);
    }

    [Test]
    [Arguments("direct")]
    [Arguments("fixed")]
    [Arguments("factory")]
    [Arguments("standard")]
    [Arguments("hedge")]
    [Arguments("selector")]
    [Arguments("partition")]
    public async Task Predicate_Is_Snapshotted_For_All_Registration_Paths(string registration)
    {
        var options = new ShieldHttpHandlerOptions { IsReplaySafeMethod = static _ => false };
        using var transport = new CountingHandler();
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("snapshot")
            .ConfigurePrimaryHttpMessageHandler(() => transport);
        var shield = HttpShield.WhenTransient().Retry(1, Backoff.None);
        using var partitions = new PartitionedShield<string, HttpResponseMessage>(_ => shield);
        using var direct = registration == "direct" ? CreateClient(transport, options) : null;
        switch (registration)
        {
            case "fixed": builder.AddShield(shield, options); break;
            case "factory": builder.AddShield(_ => shield, _ => options); break;
            case "standard": builder.AddStandardShield(standard => standard.Handler = options); break;
            case "hedge": builder.AddStandardHedgeShield(hedge => hedge.Handler = options); break;
            case "selector": builder.AddShield((_, _) => shield, options); break;
            case "partition":
                builder.AddShield(partitions, static _ => "tenant", options);
                break;
        }

        // Object registrations snapshot immediately; factories snapshot on pipeline creation.
        if (registration != "factory")
        {
            options.IsReplaySafeMethod = static _ => true;
        }

        using var provider = services.BuildServiceProvider();
        using var client = direct ?? provider.GetRequiredService<IHttpClientFactory>().CreateClient("snapshot");
        options.IsReplaySafeMethod = static _ => true;
        using var response = await client.GetAsync("https://example.test/");

        await Assert.That(transport.Attempts).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Selector_And_Partition_Options_Buffer_And_Route_Concurrent_Requests(bool partition, bool unsafeOptIn)
    {
        var options = new ShieldHttpHandlerOptions
        {
            ContentReplayPolicy = HttpContentReplayPolicy.Buffer,
            MaxBufferSize = 3,
            AllowUnsafeMethodReplay = unsafeOptIn,
            IsReplaySafeMethod = unsafeOptIn ? static _ => false : static method => method == HttpMethod.Post,
            Routing = new HttpEndpointRoutingOptions(),
        };
        options.Routing.Endpoints.Add(new HttpEndpoint(new Uri("https://alternate.test/")));
        using var transport = new CountingHandler();
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("configured")
            .ConfigurePrimaryHttpMessageHandler(() => transport);
        var shield = HttpShield.WhenTransient().Retry(1, Backoff.None);
        using var partitions = new PartitionedShield<string, HttpResponseMessage>(_ => shield);
        if (partition)
        {
            builder.AddShield(partitions, static request => request.RequestUri!.AbsolutePath, options);
        }
        else
        {
            builder.AddShield((_, _) => shield, options);
        }

        options.ContentReplayPolicy = HttpContentReplayPolicy.NoBuffer;
        options.AllowUnsafeMethodReplay = false;
        options.IsReplaySafeMethod = static _ => false;
        options.MaxBufferSize = 100;
        options.Routing.Endpoints.Clear();
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("configured");

        await Task.WhenAll(Enumerable.Range(0, 16).Select(async index =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://example.test/{index}")
            {
                Content = new StreamContent(new MemoryStream([1, 2, 3])),
            };
            using var response = await client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }));

        await Assert.That(transport.Attempts).IsEqualTo(32);
        await Assert.That(transport.AlternateAttempts).IsEqualTo(32);
        using var oversized = new HttpRequestMessage(HttpMethod.Post, "https://example.test/oversized")
        {
            Content = new StreamContent(new MemoryStream([1, 2, 3, 4])),
        };
        await Assert.That(async () => { using var response = await client.SendAsync(oversized); })
            .Throws<HttpRequestReplayException>();
        await Assert.That(transport.Attempts).IsEqualTo(32);
    }

    [Test]
    public async Task Options_Overloads_Validate_Null_Arguments_Immediately()
    {
        var builder = new ServiceCollection().AddHttpClient("guards");
        using var partitions = new PartitionedShield<string, HttpResponseMessage>(static _ => Shield<HttpResponseMessage>.Empty);
        var options = new ShieldHttpHandlerOptions();
        await Assert.That(() => ShieldHttpClientBuilderExtensions.AddShield(null!,
            static (_, _) => Shield<HttpResponseMessage>.Empty, options)).Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddShield(
            (Func<HttpRequestMessage, IServiceProvider, Shield<HttpResponseMessage>>)null!, options)).Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddShield(static (_, _) => Shield<HttpResponseMessage>.Empty, null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => ShieldHttpClientBuilderExtensions.AddShield(null!, partitions, static _ => "tenant", options))
            .Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddShield<string>(null!, static _ => "tenant", options)).Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddShield(partitions, null!, options)).Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddShield(partitions, static _ => "tenant", null!)).Throws<ArgumentNullException>();
    }

    private static HttpClient CreateClient(HttpMessageHandler transport, ShieldHttpHandlerOptions options) =>
        new(new ShieldDelegatingHandler(HttpShield.WhenTransient().Retry(1, Backoff.None), options)
        {
            InnerHandler = transport,
        });

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _attempts;
        private int _alternateAttempts;
        public int Attempts => Volatile.Read(ref _attempts);
        public int AlternateAttempts => Volatile.Read(ref _alternateAttempts);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            if (request.RequestUri!.Host == "alternate.test")
            {
                Interlocked.Increment(ref _alternateAttempts);
            }

            if (request.Content is { } content)
            {
                _ = await content.ReadAsByteArrayAsync(cancellationToken);
            }

            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }
}
