using System.Net;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

public class HttpEndpointProviderTests
{
    [Test]
    public async Task Provider_Resolves_Per_Request_And_Replaces_Static_Endpoints()
    {
        var calls = 0;
        var routing = new HttpEndpointRoutingOptions
        {
            EndpointProvider = (_, _) => new ValueTask<IReadOnlyList<HttpEndpoint>>(
                [new(new Uri($"https://endpoint-{++calls}.example"))]),
        };
        routing.Endpoints.Add(new HttpEndpoint(new Uri("https://unused.example")));
        var hosts = new List<string>();
        using var invoker = CreateInvoker(routing, (request, _) =>
        {
            hosts.Add(request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        for (var index = 0; index < 2; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example/path?q=1");
            using var response = await invoker.SendAsync(request, CancellationToken.None);
            await Assert.That(request.RequestUri!.Host).IsEqualTo("origin.example");
        }

        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(hosts).IsEquivalentTo(new[] { "endpoint-1.example", "endpoint-2.example" });
    }

    [Test]
    public async Task Empty_Result_Uses_Original_Authority()
    {
        var routing = new HttpEndpointRoutingOptions
        {
            EndpointProvider = static (_, _) => new ValueTask<IReadOnlyList<HttpEndpoint>>([]),
        };
        Uri? observed = null;
        using var invoker = CreateInvoker(routing, (request, _) =>
        {
            observed = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example:8443/path?q=1");
        using var response = await invoker.SendAsync(request, CancellationToken.None);
        await Assert.That(observed).IsEqualTo(request.RequestUri);
    }

    [Test]
    [Arguments(HttpEndpointSelectionMode.Ordered)]
    [Arguments(HttpEndpointSelectionMode.Weighted)]
    public async Task Provider_Uses_Same_Ordering_As_Static_Endpoints(HttpEndpointSelectionMode mode)
    {
        HttpEndpoint[] endpoints = [
            new(new Uri("https://first.example"), weight: 1),
            new(new Uri("https://second.example"), weight: 20),
            new(new Uri("https://third.example"), weight: 5),
        ];
        var expected = await CaptureOrder(mode, endpoints, dynamic: false);
        var actual = await CaptureOrder(mode, endpoints, dynamic: true);
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        await Assert.That(actual.Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Linked_Cancellation_Stops_Provider_Before_Transport(bool requestOption)
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<IReadOnlyList<HttpEndpoint>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var routing = new HttpEndpointRoutingOptions
        {
            EndpointProvider = (_, token) =>
            {
                entered.SetResult(token);
                return new ValueTask<IReadOnlyList<HttpEndpoint>>(completion.Task);
            },
        };
        var sends = 0;
        using var invoker = CreateInvoker(routing, (_, _) =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example");
        if (requestOption) { request.WithKevlarCancellationToken(cancellation.Token); }
        var send = invoker.SendAsync(request, requestOption ? CancellationToken.None : cancellation.Token);
        var providerToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try
        {
            await Assert.That(async () => await send.WaitAsync(TimeSpan.FromSeconds(5)))
                .Throws<OperationCanceledException>();
            await Assert.That(providerToken.IsCancellationRequested).IsTrue();
            await Assert.That(sends).IsEqualTo(0);
        }
        finally
        {
            completion.TrySetResult([]);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Invalid_Provider_Result_Fails_Before_Transport(bool nullEntry)
    {
        var routing = new HttpEndpointRoutingOptions
        {
            EndpointProvider = (_, _) => new ValueTask<IReadOnlyList<HttpEndpoint>>(
                nullEntry ? [null!] : null!),
        };
        var sends = 0;
        using var invoker = CreateInvoker(routing, (_, _) =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example");
        await Assert.That(async () => await invoker.SendAsync(request, CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(sends).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Replay_Opt_In_And_Request_Metadata_Are_Preserved(bool allowReplay)
    {
        var resolutions = 0;
        var routing = new HttpEndpointRoutingOptions
        {
            EndpointProvider = (_, _) =>
            {
                resolutions++;
                return new ValueTask<IReadOnlyList<HttpEndpoint>>([
                    new(new Uri("https://first.example")), new(new Uri("https://second.example")),
                ]);
            },
        };
        var observations = new List<(string Host, string Path, string Body, string Header)>();
        using var invoker = CreateInvoker(routing, async (request, _) =>
        {
            observations.Add((request.RequestUri!.Host, request.RequestUri.PathAndQuery,
                await request.Content!.ReadAsStringAsync(), request.Headers.GetValues("x-marker").Single()));
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }, HttpShield.WhenTransient().Retry(1, Backoff.None));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example/path?q=1")
        {
            Content = new StringContent("body"),
        };
        request.Headers.Add("x-marker", "marker");
        if (allowReplay) { request.AllowReplay(); }
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(resolutions).IsEqualTo(1);
        await Assert.That(observations.Count).IsEqualTo(allowReplay ? 2 : 1);
        await Assert.That(observations.All(item => item.Path == "/path?q=1"
            && item.Body == "body" && item.Header == "marker")).IsTrue();
        if (allowReplay) { await Assert.That(observations[1].Host).IsEqualTo("second.example"); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Registration_Snapshots_Provider_Delegate(bool standardHedge)
    {
        var routing = new HttpEndpointRoutingOptions
        {
            EndpointProvider = static (_, _) => new ValueTask<IReadOnlyList<HttpEndpoint>>(
                [new(new Uri("https://resolved.example"))]),
        };
        string? host = null;
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("dynamic").ConfigurePrimaryHttpMessageHandler(() =>
            new DelegateHandler((request, _) =>
            {
                host = request.RequestUri!.Host;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }));
        if (standardHedge) { builder.AddStandardHedgeShield(options => options.Routing = routing); }
        else { builder.AddShield(Shield<HttpResponseMessage>.Empty, new ShieldHttpHandlerOptions { Routing = routing }); }
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("dynamic");
        routing.EndpointProvider = static (_, _) => throw new InvalidOperationException("Mutated delegate was used.");
        using var response = await client.GetAsync("https://origin.example");
        await Assert.That(host).IsEqualTo("resolved.example");
    }

    [Test]
    public async Task Asynchronous_Provider_Is_Called_Once_And_Hedges_Keep_Its_Snapshot()
    {
        var endpoints = new List<HttpEndpoint>
        {
            new(new Uri("https://first.example")), new(new Uri("https://second.example")),
        };
        var resolutions = 0;
        var routing = new HttpEndpointRoutingOptions
        {
            EndpointProvider = async (_, _) =>
            {
                resolutions++;
                await Task.Yield();
                return endpoints;
            },
        };
        var hosts = new List<string>();
        using var invoker = CreateInvoker(routing, (request, _) =>
        {
            hosts.Add(request.RequestUri!.Host);
            endpoints.Clear();
            return Task.FromResult(new HttpResponseMessage(
                hosts.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        }, HttpShield.WhenTransient().Hedge(1, TimeSpan.Zero));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(resolutions).IsEqualTo(1);
        await Assert.That(hosts.SequenceEqual(new[] { "first.example", "second.example" })).IsTrue();
    }

    [Test]
    public async Task Provider_Exception_Is_Propagated_Without_Retry_Or_Transport()
    {
        var failure = new InvalidOperationException("lookup failed");
        var calls = 0;
        var routing = new HttpEndpointRoutingOptions
        {
            EndpointProvider = (_, _) =>
            {
                calls++;
                throw failure;
            },
        };
        var sends = 0;
        using var invoker = CreateInvoker(routing, (_, _) =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }, Shield.For<HttpResponseMessage>().Retry(3, Backoff.None));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example");
        var observed = await Assert.That(async () => await invoker.SendAsync(request, CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(observed).IsSameReferenceAs(failure);
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(sends).IsEqualTo(0);
    }

    private static async Task<string[]> CaptureOrder(HttpEndpointSelectionMode mode, HttpEndpoint[] endpoints, bool dynamic)
    {
        var routing = new HttpEndpointRoutingOptions { SelectionMode = mode, Seed = 1729 };
        var resolutions = 0;
        if (dynamic)
        {
            routing.EndpointProvider = (_, _) =>
            {
                resolutions++;
                return new ValueTask<IReadOnlyList<HttpEndpoint>>(endpoints);
            };
        }
        else
        {
            foreach (var endpoint in endpoints) { routing.Endpoints.Add(endpoint); }
        }
        var hosts = new List<string>();
        using var invoker = CreateInvoker(routing, (request, _) =>
        {
            hosts.Add(request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }, HttpShield.WhenTransient().Retry(2, Backoff.None));
        for (var index = 0; index < 3; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example");
            using var response = await invoker.SendAsync(request, CancellationToken.None);
        }
        await Assert.That(resolutions).IsEqualTo(dynamic ? 3 : 0);
        return hosts.ToArray();
    }

    private static HttpMessageInvoker CreateInvoker(
        HttpEndpointRoutingOptions routing,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        Shield<HttpResponseMessage>? shield = null) =>
        new(new ShieldDelegatingHandler(shield ?? Shield<HttpResponseMessage>.Empty,
            new ShieldHttpHandlerOptions { Routing = routing }) { InnerHandler = new DelegateHandler(send) });

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
