using System.Net;
using BenchmarkDotNet.Attributes;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Benchmarks;

/// <summary>Steady-state request selection and partition lookup, with and without logging decoration.</summary>
[MemoryDiagnoser]
public class HttpDecorationBenchmarks
{
    private ServiceProvider _services = null!;
    private PartitionedShield<string, HttpResponseMessage> _partitions = null!;
    private HttpClient _selector = null!;
    private HttpClient _partition = null!;
    private readonly Uri _uri = new("https://benchmark.invalid/");

    [Params(false, true)]
    public bool LoggingEnabled { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        if (LoggingEnabled)
        {
            services.AddKevlarLogging();
        }
        var shared = HttpShield.WhenTransient()
            .CircuitBreaker(consecutiveFailures: 100, breakDuration: TimeSpan.FromMinutes(1));
        _partitions = new PartitionedShield<string, HttpResponseMessage>(static _ =>
            HttpShield.WhenTransient()
                .CircuitBreaker(consecutiveFailures: 100, breakDuration: TimeSpan.FromMinutes(1)));
        services.AddHttpClient("selector")
            .ConfigurePrimaryHttpMessageHandler(static () => new SuccessHandler())
            .AddShield((_, _) => shared);
        services.AddHttpClient("partition")
            .ConfigurePrimaryHttpMessageHandler(static () => new SuccessHandler())
            .AddShield(_partitions, static _ => "tenant");
        _services = services.BuildServiceProvider();
        var factory = _services.GetRequiredService<IHttpClientFactory>();
        _selector = factory.CreateClient("selector");
        _partition = factory.CreateClient("partition");
        // Measure cached hits after initial handler and partition creation.
        await RequestSelector();
        await PartitionSelector();
    }

    [Benchmark]
    public async Task<HttpStatusCode> RequestSelector()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        using var response = await _selector.SendAsync(request);
        return response.StatusCode;
    }

    [Benchmark]
    public async Task<HttpStatusCode> PartitionSelector()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        using var response = await _partition.SendAsync(request);
        return response.StatusCode;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _selector.Dispose();
        _partition.Dispose();
        _services.Dispose();
        _partitions.Dispose();
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
