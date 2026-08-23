using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Benchmarks;

/// <summary>Standard endpoint-aware HTTP hedging registration overhead.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class HttpStandardHedgingBenchmarks
{
    private ServiceProvider _services = null!;
    private HttpClient _manual = null!;
    private HttpClient _standard = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("manual")
            .ConfigurePrimaryHttpMessageHandler(static () => new SuccessHandler())
            .AddShield(CreateOuterShield(), CreateHandlerOptions());
        services.AddHttpClient("standard")
            .ConfigurePrimaryHttpMessageHandler(static () => new SuccessHandler())
            .AddStandardHedgingShield(static options =>
            {
                options.Endpoints.Add(new HttpEndpoint(new Uri("https://first.invalid")));
                options.Endpoints.Add(new HttpEndpoint(new Uri("https://second.invalid")));
            });
        _services = services.BuildServiceProvider();
        var factory = _services.GetRequiredService<IHttpClientFactory>();
        _manual = factory.CreateClient("manual");
        _standard = factory.CreateClient("standard");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _manual.Dispose();
        _standard.Dispose();
        _services.Dispose();
    }

    [BenchmarkCategory("HttpStandardHedgingHappy"), Benchmark(Baseline = true)]
    public async Task ManualComposition()
    {
        using var response = await _manual.GetAsync("https://origin.invalid/");
    }

    [BenchmarkCategory("HttpStandardHedgingHappy"), Benchmark]
    public async Task StandardRegistration()
    {
        using var response = await _standard.GetAsync("https://origin.invalid/");
    }

    private static Shield<HttpResponseMessage> CreateOuterShield() =>
        Shield.Timeout(TimeSpan.FromSeconds(30))
            .For<HttpResponseMessage>()
            .When<HttpRequestException>()
            .Or<TimeoutExceededException>()
            .Or<ConcurrencyLimitExceededException>()
            .Or<CircuitOpenException>()
            .OrResult(HttpShield.IsTransient)
            .Hedge(2, TimeSpan.FromSeconds(1));

    private static ShieldHttpHandlerOptions CreateHandlerOptions()
    {
        var routing = new HttpEndpointRoutingOptions
        {
            ShieldFactory = static _ => HttpShield.WhenTransient()
                .ConcurrencyLimit(10)
                .CircuitBreaker(options =>
                {
                    options.FailureRatio = 0.5;
                    options.MinimumThroughput = 10;
                    options.SamplingWindow = TimeSpan.FromSeconds(30);
                    options.BreakDuration = TimeSpan.FromSeconds(15);
                })
                .Timeout(TimeSpan.FromSeconds(10)),
        };
        routing.Endpoints.Add(new HttpEndpoint(new Uri("https://first.invalid")));
        routing.Endpoints.Add(new HttpEndpoint(new Uri("https://second.invalid")));
        return new ShieldHttpHandlerOptions { Routing = routing };
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
