using System.Diagnostics;
using System.Net;
using Kevlar.Extensions.Http;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.IntegrationTests;

/// <summary>
/// End-to-end HTTP tests over real loopback sockets: genuine requests, cancellation,
/// headers and connection behaviour, driven through production-style shield mixes.
/// </summary>
public class HttpResilienceTests
{
    private static readonly HttpClient Client = new();

    [Test]
    public async Task Standard_Web_Mix_Recovers_From_Transient_Failures()
    {
        // Real-world mix: total timeout → retry transient → per-attempt timeout.
        await using var server = FlakyHttpServer.Start((call, context) => call switch
        {
            1 => FlakyHttpServer.Respond(context, 500),
            2 => FlakyHttpServer.Respond(context, 503),
            _ => FlakyHttpServer.Respond(context, 200, "hello"),
        });

        var shield = Shield.Timeout(TimeSpan.FromSeconds(30))
            .For<HttpResponseMessage>()
            .When<HttpRequestException>()
            .Or<TimeoutExceededException>()
            .OrResult(HttpShield.IsTransient)
            .Retry(options =>
            {
                options.MaxRetries = 3;
                options.Backoff = Backoff.None;
            })
            .Timeout(TimeSpan.FromSeconds(5));

        using var response = await shield.ExecuteAsync(ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct)));

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("hello");
        await Assert.That(server.CallCount).IsEqualTo(3);
    }

    [Test]
    public async Task Attempt_Timeout_With_Retry_Beats_A_Slow_First_Response()
    {
        // First request hangs for 5s; the 300ms attempt timeout cancels it and the retry succeeds.
        await using var server = FlakyHttpServer.Start(async (call, context) =>
        {
            if (call == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await FlakyHttpServer.Respond(context, 200, "slow");
            }
            else
            {
                await FlakyHttpServer.Respond(context, 200, "recovered");
            }
        });

        var shield = Shield.For<HttpResponseMessage>()
            .When<TimeoutExceededException>()
            .Retry(2, Backoff.None)
            .Timeout(TimeSpan.FromMilliseconds(300));

        var stopwatch = Stopwatch.StartNew();
        using var response = await shield.ExecuteAsync(ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct)));
        stopwatch.Stop();

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("recovered");
        await Assert.That(stopwatch.Elapsed < TimeSpan.FromSeconds(4)).IsTrue();
        await Assert.That(server.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task Attempt_Timeout_Retries_A_Transport_Wrapped_Cancellation()
    {
        var handler = new WrappedCancellationHandler();
        using var client = new HttpClient(handler)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
        var shield = Shield.For<HttpResponseMessage>()
            .When<TimeoutExceededException>()
            .Retry(1, Backoff.None)
            .Timeout(TimeSpan.FromMilliseconds(20));

        using var response = await shield.ExecuteAsync(cancellationToken =>
            new ValueTask<HttpResponseMessage>(client.GetAsync("http://localhost/", cancellationToken)));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(handler.Attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Circuit_Breaker_Fails_Fast_Without_Hitting_The_Server()
    {
        await using var server = FlakyHttpServer.Start((_, context) => FlakyHttpServer.Respond(context, 500));

        var shield = HttpShield.WhenTransient().CircuitBreaker(consecutiveFailures: 3, breakDuration: TimeSpan.FromMinutes(1));

        for (var i = 0; i < 3; i++)
        {
            using var response = await shield.ExecuteAsync(ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct)));
            await Assert.That((int)response.StatusCode).IsEqualTo(500);
        }

        var stopwatch = Stopwatch.StartNew();
        await Assert.That(async () => await shield.ExecuteAsync(ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct))))
            .Throws<CircuitOpenException>();
        stopwatch.Stop();

        // Rejected instantly, and the server never saw a fourth request.
        await Assert.That(server.CallCount).IsEqualTo(3);
        await Assert.That(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500)).IsTrue();
    }

    [Test]
    public async Task RetryAfter_Header_Paces_The_Retry_End_To_End()
    {
        await using var server = FlakyHttpServer.Start((call, context) => call == 1
            ? FlakyHttpServer.Respond(context, 429, retryAfterSeconds: "1")
            : FlakyHttpServer.Respond(context, 200, "after backpressure"));

        var shield = HttpShield.WhenTransient().Retry(options =>
        {
            options.MaxRetries = 2;
            options.Backoff = Backoff.None;
            options.DelayGenerator = HttpShield.RetryAfter;
        });

        var stopwatch = Stopwatch.StartNew();
        using var response = await shield.ExecuteAsync(ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct)));
        stopwatch.Stop();

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("after backpressure");
        await Assert.That(server.CallCount).IsEqualTo(2);
        await Assert.That(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(800)).IsTrue();
    }

    [Test]
    public async Task Hedging_Races_A_Second_Request_Past_A_Slow_Primary()
    {
        await using var server = FlakyHttpServer.Start(async (call, context) =>
        {
            if (call == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                await FlakyHttpServer.Respond(context, 200, "slow");
            }
            else
            {
                await FlakyHttpServer.Respond(context, 200, "fast");
            }
        });

        var shield = Shield.For<HttpResponseMessage>()
            .When<HttpRequestException>()
            .OrResult(HttpShield.IsTransient)
            .Hedge(maxHedgedAttempts: 1, delay: TimeSpan.FromMilliseconds(100));

        var stopwatch = Stopwatch.StartNew();
        using var response = await shield.ExecuteAsync(ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct)));
        stopwatch.Stop();

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("fast");
        await Assert.That(stopwatch.Elapsed < TimeSpan.FromSeconds(2.5)).IsTrue();
        await Assert.That(server.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task HttpClientFactory_Standard_Pipeline_Survives_A_Flaky_Backend()
    {
        await using var server = FlakyHttpServer.Start((call, context) => call switch
        {
            1 => FlakyHttpServer.Respond(context, 502),
            _ => FlakyHttpServer.Respond(context, 200, "factory ok"),
        });

        var services = new ServiceCollection();
        services.AddHttpClient("backend")
            .AddShield(HttpShield.WhenTransient().Retry(options =>
            {
                options.MaxRetries = 2;
                options.Backoff = Backoff.None;
            }));

        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("backend");

        using var response = await client.GetAsync(server.Url);

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("factory ok");
        await Assert.That(server.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task HttpClientFactory_Can_Use_Dynamic_Registry_Shield()
    {
        await using var server = FlakyHttpServer.Start((call, context) => call == 1
            ? FlakyHttpServer.Respond(context, 503)
            : FlakyHttpServer.Respond(context, 200, "dynamic registry"));
        var services = new ServiceCollection();
        services.AddKevlar();
        services.AddHttpClient("dynamic-registry")
            .AddShield(serviceProvider => serviceProvider
                .GetRequiredService<IKevlarRegistry>()
                .GetOrAdd("http-dynamic", _ => HttpShield.WhenTransient().Retry(1, Backoff.None)));
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("dynamic-registry");

        using var response = await client.GetAsync(server.Url);

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("dynamic registry");
        await Assert.That(server.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task HttpClientFactory_Standard_Hedge_Routes_Across_Loopback_Endpoints()
    {
        await using var failing = FlakyHttpServer.Start((_, context) =>
            FlakyHttpServer.Respond(context, 503));
        await using var healthy = FlakyHttpServer.Start((_, context) =>
            FlakyHttpServer.Respond(context, 200, "hedged ok"));
        using var services = new ServiceCollection()
            .AddHttpClient("hedged-backend")
            .AddStandardHedgeShield(options =>
            {
                options.Routing = new HttpEndpointRoutingOptions();
                options.Routing.Endpoints.Add(new HttpEndpoint(new Uri(failing.Url)));
                options.Routing.Endpoints.Add(new HttpEndpoint(new Uri(healthy.Url)));
                options.Hedge.Delay = Timeout.InfiniteTimeSpan;
            })
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("hedged-backend");

        using var response = await client.GetAsync("http://origin.invalid/path?q=1");

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("hedged ok");
        await Assert.That(failing.CallCount).IsEqualTo(1);
        await Assert.That(healthy.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task HttpClientFactory_Standard_Hedge_Reuses_The_Request_Authority()
    {
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = FlakyHttpServer.Start(async (call, context) =>
        {
            if (call == 1)
            {
                await releaseFirst.Task;
                await FlakyHttpServer.Respond(context, 200, "slow");
                return;
            }

            await FlakyHttpServer.Respond(context, 200, "same authority");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            releaseFirst.TrySetResult();
        });
        using var services = new ServiceCollection()
            .AddHttpClient("same-authority-hedge")
            .AddStandardHedgeShield(options => options.Hedge.Delay = TimeSpan.Zero)
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>()
            .CreateClient("same-authority-hedge");

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.GetAsync($"{server.Url}same-authority?q=1");
        stopwatch.Stop();

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("same authority");
        await Assert.That(server.CallCount).IsEqualTo(2);
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(2.5));
    }

    [Test]
    public async Task HttpClientFactory_Same_Authority_Hedge_Cancels_And_Disposes_The_Loser()
    {
        var transport = new SameAuthorityHedgeHandler();
        using var services = new ServiceCollection()
            .AddHttpClient("same-authority-cleanup")
            .ConfigurePrimaryHttpMessageHandler(() => transport)
            .AddStandardHedgeShield(options => options.Hedge.Delay = TimeSpan.Zero)
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>()
            .CreateClient("same-authority-cleanup");

        using var response = await client.GetAsync("https://origin.example/path?q=1");

        await transport.LoserCancelled.WaitAsync(TimeSpan.FromSeconds(5));
        await transport.LoserDisposed.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("winner");
        await Assert.That(transport.Authorities).IsEquivalentTo(
            ["https://origin.example", "https://origin.example"]);
    }

    [Test]
    public async Task HttpClientFactory_Standard_Hedge_Uses_Adaptive_Delay()
    {
        await using var slow = FlakyHttpServer.Start(async (_, context) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            await FlakyHttpServer.Respond(context, 200, "slow");
        });
        await using var healthy = FlakyHttpServer.Start((_, context) =>
            FlakyHttpServer.Respond(context, 200, "adaptive hedge"));
        var observedAttempt = 0;
        using var services = new ServiceCollection()
            .AddHttpClient("adaptive-hedge")
            .AddStandardHedgeShield(options =>
            {
                options.Routing = new HttpEndpointRoutingOptions();
                options.Routing.Endpoints.Add(new HttpEndpoint(new Uri(slow.Url)));
                options.Routing.Endpoints.Add(new HttpEndpoint(new Uri(healthy.Url)));
                options.Hedge.Delay = TimeSpan.FromMinutes(1);
                options.Hedge.DelayGenerator = hedge =>
                {
                    observedAttempt = hedge.AttemptNumber;
                    return new(TimeSpan.Zero);
                };
            })
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("adaptive-hedge");

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.GetAsync("http://origin.invalid/adaptive");
        stopwatch.Stop();

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("adaptive hedge");
        await Assert.That(observedAttempt).IsEqualTo(1);
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(2.5));
        await Assert.That(slow.CallCount).IsEqualTo(1);
        await Assert.That(healthy.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Caller_Cancellation_Stops_Loopback_Retry_And_Preserves_The_Token()
    {
        var requestStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = FlakyHttpServer.Start(async (_, context) =>
        {
            requestStarted.TrySetResult();
            await release.Task;
            await FlakyHttpServer.Respond(context, 200, "late");
        });
        var shield = HttpShield.WhenTransient().Retry(3, Backoff.None);
        using var cancellation = new CancellationTokenSource();

        var send = shield.ExecuteAsync(
            ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct)),
            cancellation.Token).AsTask();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        var exception = await Assert.That(async () => await send)
            .Throws<OperationCanceledException>();
        release.TrySetResult();

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(server.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task HttpClientFactory_Factories_Resolve_Services_And_Apply_Handler_Options()
    {
        await using var server = FlakyHttpServer.Start((call, context) => call == 1
            ? FlakyHttpServer.Respond(context, 503)
            : FlakyHttpServer.Respond(context, 200, "factory options ok"));
        var marker = new FactoryMarker();
        var shieldFactoryCalls = 0;
        var optionsFactoryCalls = 0;
        using var services = new ServiceCollection()
            .AddSingleton(marker)
            .AddHttpClient("factory-options")
            .AddShield(
                provider =>
                {
                    _ = provider.GetRequiredService<FactoryMarker>();
                    Interlocked.Increment(ref shieldFactoryCalls);
                    return HttpShield.WhenTransient().Retry(1, Backoff.None);
                },
                provider =>
                {
                    _ = provider.GetRequiredService<FactoryMarker>();
                    Interlocked.Increment(ref optionsFactoryCalls);
                    return new ShieldHttpHandlerOptions();
                })
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>()
            .CreateClient("factory-options");

        using var response = await client.GetAsync(server.Url);

        await Assert.That(await response.Content.ReadAsStringAsync())
            .IsEqualTo("factory options ok");
        await Assert.That(server.CallCount).IsEqualTo(2);
        await Assert.That(shieldFactoryCalls).IsEqualTo(1);
        await Assert.That(optionsFactoryCalls).IsEqualTo(1);
    }

    private sealed class FactoryMarker;

    private sealed class WrappedCancellationHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts == 1)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw new TaskCanceledException("transport wrapper");
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class SameAuthorityHedgeHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _firstStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _loserCancelled = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TrackingContent _loserContent = new();
        private int _attempts;

        public List<string> Authorities { get; } = [];

        public Task LoserCancelled => _loserCancelled.Task;

        public Task LoserDisposed => _loserContent.Disposed;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (Authorities)
            {
                Authorities.Add(request.RequestUri!.GetLeftPart(UriPartial.Authority));
            }

            if (Interlocked.Increment(ref _attempts) == 1)
            {
                _firstStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _loserCancelled.TrySetResult();
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = _loserContent,
                };
            }

            await _firstStarted.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("winner"),
            };
        }
    }

    private sealed class TrackingContent : HttpContent
    {
        private readonly TaskCompletionSource _disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Disposed => _disposed.Task;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed.TrySetResult();
            }

            base.Dispose(disposing);
        }
    }
}
