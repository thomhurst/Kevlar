using System.Net;
using Kevlar.Extensions.Http;
using Kevlar.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly.CircuitBreaker;

namespace Kevlar.IntegrationTests;

public class MigrationParityTests
{
    [Test]
    public async Task Standard_Http_Pipelines_Retry_503_503_200_Equally()
    {
        await using var pollyServer = FlakyHttpServer.Start((call, context) =>
            FlakyHttpServer.Respond(context, call < 3 ? 503 : 200));
        await using var kevlarServer = FlakyHttpServer.Start((call, context) =>
            FlakyHttpServer.Respond(context, call < 3 ? 503 : 200));
        using var pollyServices = BuildPollyServices(maxRetries: 2, minimumThroughput: 100);
        using var kevlarServices = BuildKevlarServices(maxRetries: 2, minimumThroughput: 100);
        using var pollyClient = pollyServices.GetRequiredService<IHttpClientFactory>().CreateClient("polly");
        using var kevlarClient = kevlarServices.GetRequiredService<IHttpClientFactory>().CreateClient("kevlar");

        using var pollyResponse = await pollyClient.GetAsync(pollyServer.Url);
        using var kevlarResponse = await kevlarClient.GetAsync(kevlarServer.Url);

        await Assert.That(pollyResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(kevlarResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(pollyServer.CallCount).IsEqualTo(3);
        await Assert.That(kevlarServer.CallCount).IsEqualTo(3);
    }

    [Test]
    public async Task Standard_Http_Pipelines_Trip_After_Five_503_Responses()
    {
        await using var pollyServer = FlakyHttpServer.Start((_, context) =>
            FlakyHttpServer.Respond(context, 503));
        await using var kevlarServer = FlakyHttpServer.Start((_, context) =>
            FlakyHttpServer.Respond(context, 503));
        using var pollyServices = BuildPollyServices(maxRetries: 0, minimumThroughput: 5);
        using var kevlarServices = BuildKevlarServices(maxRetries: 0, minimumThroughput: 5);
        using var pollyClient = pollyServices.GetRequiredService<IHttpClientFactory>().CreateClient("polly");
        using var kevlarClient = kevlarServices.GetRequiredService<IHttpClientFactory>().CreateClient("kevlar");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var pollyResponse = await pollyClient.GetAsync(pollyServer.Url);
            using var kevlarResponse = await kevlarClient.GetAsync(kevlarServer.Url);
            await Assert.That(pollyResponse.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(kevlarResponse.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }

        await Assert.That(async () => await pollyClient.GetAsync(pollyServer.Url))
            .Throws<BrokenCircuitException>();
        await Assert.That(async () => await kevlarClient.GetAsync(kevlarServer.Url))
            .Throws<CircuitOpenException>();
        await Assert.That(pollyServer.CallCount).IsEqualTo(5);
        await Assert.That(kevlarServer.CallCount).IsEqualTo(5);
    }

    private static ServiceProvider BuildPollyServices(int maxRetries, int minimumThroughput) =>
        new ServiceCollection()
            .AddHttpClient("polly")
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = Math.Max(maxRetries, 1);
                if (maxRetries == 0)
                {
                    options.Retry.ShouldHandle = static _ => new ValueTask<bool>(false);
                }
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
                options.CircuitBreaker.FailureRatio = 1;
                options.CircuitBreaker.MinimumThroughput = minimumThroughput;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(1);
            })
            .Services
            .BuildServiceProvider();

    private static ServiceProvider BuildKevlarServices(int maxRetries, int minimumThroughput) =>
        new ServiceCollection()
            .AddHttpClient("kevlar")
            .AddStandardShield(options =>
            {
                options.Retry.MaxRetries = maxRetries;
                options.Retry.Backoff = Backoff.None;
                options.CircuitBreaker.FailureRatio = 1;
                options.CircuitBreaker.MinimumThroughput = minimumThroughput;
                options.CircuitBreaker.SamplingWindow = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(1);
            })
            .Services
            .BuildServiceProvider();
}
