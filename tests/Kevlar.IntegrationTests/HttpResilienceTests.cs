using System.Diagnostics;
using Kevlar.Extensions.Http;
using Kevlar.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.IntegrationTests;

/// <summary>
/// End-to-end HTTP tests over real loopback sockets: genuine requests, cancellation,
/// headers and connection behaviour, driven through production-style policy mixes.
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

        var policy = Policy.Timeout(TimeSpan.FromSeconds(30))
            .For<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TimeoutExceededException>()
            .HandleResult(HttpKevlar.IsTransient)
            .Retry(options =>
            {
                options.MaxRetries = 3;
                options.Backoff = Backoff.None;
                options.OnRetry = retry => (retry.Result as HttpResponseMessage)?.Dispose();
            })
            .Timeout(TimeSpan.FromSeconds(5));

        using var response = await policy.ExecuteAsync(ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct)));

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

        var policy = Policy.For<HttpResponseMessage>()
            .Handle<TimeoutExceededException>()
            .Retry(2, Backoff.None)
            .Timeout(TimeSpan.FromMilliseconds(300));

        var stopwatch = Stopwatch.StartNew();
        using var response = await policy.ExecuteAsync(ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct)));
        stopwatch.Stop();

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("recovered");
        await Assert.That(stopwatch.Elapsed < TimeSpan.FromSeconds(4)).IsTrue();
        await Assert.That(server.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task Circuit_Breaker_Fails_Fast_Without_Hitting_The_Server()
    {
        await using var server = FlakyHttpServer.Start((_, context) => FlakyHttpServer.Respond(context, 500));

        var policy = HttpKevlar.HandleTransient().CircuitBreaker(consecutiveFailures: 3, breakDuration: TimeSpan.FromMinutes(1));

        for (var i = 0; i < 3; i++)
        {
            using var response = await policy.ExecuteAsync(ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct)));
            await Assert.That((int)response.StatusCode).IsEqualTo(500);
        }

        var stopwatch = Stopwatch.StartNew();
        await Assert.That(async () => await policy.ExecuteAsync(ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct))))
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

        var policy = HttpKevlar.HandleTransient().Retry(options =>
        {
            options.MaxRetries = 2;
            options.Backoff = Backoff.None;
            options.DelayGenerator = HttpKevlar.RetryAfter;
            options.OnRetry = retry => (retry.Result as HttpResponseMessage)?.Dispose();
        });

        var stopwatch = Stopwatch.StartNew();
        using var response = await policy.ExecuteAsync(ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct)));
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

        var policy = Policy.For<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(HttpKevlar.IsTransient)
            .Hedge(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(100));

        var stopwatch = Stopwatch.StartNew();
        using var response = await policy.ExecuteAsync(ct => new ValueTask<HttpResponseMessage>(Client.GetAsync(server.Url, ct)));
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
            .AddKevlar(HttpKevlar.HandleTransient().Retry(options =>
            {
                options.MaxRetries = 2;
                options.Backoff = Backoff.None;
                options.OnRetry = retry => (retry.Result as HttpResponseMessage)?.Dispose();
            }));

        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("backend");

        using var response = await client.GetAsync(server.Url);

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("factory ok");
        await Assert.That(server.CallCount).IsEqualTo(2);
    }
}
