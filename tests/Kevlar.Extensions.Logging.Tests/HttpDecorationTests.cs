using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Extensions.Logging.Tests;

public class HttpDecorationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Reusing_Selected_Shield_Keeps_Breaker_Telemetry_Registration_Count_Constant(bool partitioned)
    {
        var shared = HttpShield.WhenTransient()
            .CircuitBreaker(consecutiveFailures: 100, breakDuration: TimeSpan.FromMinutes(1));
        using var partitions = new PartitionedShield<string, HttpResponseMessage>(_ => shared);
        var retained = new RetainingDecorator();
        var services = new ServiceCollection();
        services.AddKevlarLogging();
        // Retain every decorated shield so GC cannot conceal repeated logging registrations.
        services.AddSingleton<IShieldDecorator>(retained);
        var builder = services.AddHttpClient("api")
            .ConfigurePrimaryHttpMessageHandler(static () => new SuccessHandler());
        if (partitioned)
        {
            builder.AddShield(partitions, static _ => "tenant");
        }
        else
        {
            builder.AddShield((_, _) => shared);
        }
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("api");
        using (var response = await client.GetAsync("https://dependency.test/"))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        var initialRegistrations = TelemetryRegistrationCount(shared);
        await Assert.That(initialRegistrations).IsGreaterThan(0);

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            using var response = await client.GetAsync("https://dependency.test/");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        })));

        await Assert.That(TelemetryRegistrationCount(shared)).IsEqualTo(initialRegistrations);
        await Assert.That(retained.Shields.Count).IsEqualTo(1);
        GC.KeepAlive(retained);
    }

    private static int TelemetryRegistrationCount(Shield<HttpResponseMessage> shield)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var strategies = (Array)typeof(Shield<HttpResponseMessage>).GetField("Strategies", flags)!.GetValue(shield)!;
        var breaker = strategies.Cast<object>().Single(strategy => strategy.GetType().Name == "CircuitBreakerStrategy");
        var core = breaker.GetType().GetField("_core", flags)!.GetValue(breaker)!;
        return ((Array)core.GetType().GetField("_telemetryRegistrations", flags)!.GetValue(core)!).Length;
    }

    private sealed class RetainingDecorator : IShieldDecorator
    {
        public ConcurrentBag<object> Shields { get; } = new();

        public Shield Decorate(Shield shield, string? name) => throw new NotSupportedException();

        public Shield<TResult> Decorate<TResult>(Shield<TResult> shield, string? name)
        {
            Shields.Add(shield);
            return shield;
        }
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
