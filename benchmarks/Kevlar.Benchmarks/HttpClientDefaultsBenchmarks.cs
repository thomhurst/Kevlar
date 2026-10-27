using System.Net;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Benchmarks;

/// <summary>Request costs for standard shields registered globally or for one named client.</summary>
[MemoryDiagnoser]
public class HttpClientDefaultsBenchmarks
{
    private ServiceProvider _services = null!;
    private HttpClient _client = null!;
    private readonly Uri _uri = new("https://dependency.invalid/");

    [Params(false, true)]
    public bool UseDefaults { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        if (UseDefaults)
        {
            services.ConfigureHttpClientDefaults(builder => builder.AddStandardShield());
        }

        var builder = services.AddHttpClient("client")
            .ConfigurePrimaryHttpMessageHandler(static () => new SuccessHandler());
        if (!UseDefaults)
        {
            builder.AddStandardShield();
        }

        _services = services.BuildServiceProvider();
        _client = _services.GetRequiredService<IHttpClientFactory>().CreateClient("client");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _services.Dispose();
    }

    [Benchmark]
    public async Task<HttpStatusCode> Send()
    {
        using var response = await _client.GetAsync(_uri);
        return response.StatusCode;
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
