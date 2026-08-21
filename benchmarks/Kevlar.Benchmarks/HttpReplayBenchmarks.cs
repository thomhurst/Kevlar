using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Kevlar.Extensions.Http;

namespace Kevlar.Benchmarks;

/// <summary>HTTP handler overhead for direct, buffered, and request-factory paths.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class HttpReplayBenchmarks
{
    private readonly HttpMessageInvoker _direct = CreateInvoker(
        Shield<HttpResponseMessage>.Empty,
        new ShieldHttpHandlerOptions(),
        retry: false);
    private readonly HttpMessageInvoker _standard = CreateInvoker(
        HttpShield.Standard(),
        new ShieldHttpHandlerOptions(),
        retry: false);
    private readonly HttpMessageInvoker _buffered = CreateInvoker(
        HttpShield.WhenTransient().Retry(1, Backoff.None),
        new ShieldHttpHandlerOptions
        {
            ContentReplayPolicy = HttpContentReplayPolicy.Buffer,
            AllowUnsafeMethodReplay = true,
        },
        retry: true);
    private readonly HttpMessageInvoker _factory = CreateInvoker(
        HttpShield.WhenTransient().Retry(1, Backoff.None),
        new ShieldHttpHandlerOptions
        {
            RequestFactory = static (original, _, _) => new ValueTask<HttpRequestMessage>(
                new HttpRequestMessage(original.Method, original.RequestUri)
                {
                    Content = new StringContent("payload"),
                }),
        },
        retry: true);

    [BenchmarkCategory("HttpNoContent"), Benchmark]
    public async Task Direct_NoContent()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://benchmark.invalid/");
        using var response = await _direct.SendAsync(request, CancellationToken.None);
    }

    [BenchmarkCategory("HttpNoContent"), Benchmark]
    public async Task Standard_NoContent()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://benchmark.invalid/");
        using var response = await _standard.SendAsync(request, CancellationToken.None);
    }

    [BenchmarkCategory("HttpBufferedRetry"), Benchmark]
    public async Task BufferedContent_WithRetry()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://benchmark.invalid/")
        {
            Content = new StringContent("payload"),
        };
        using var response = await _buffered.SendAsync(request, CancellationToken.None);
    }

    [BenchmarkCategory("HttpFactoryRetry"), Benchmark]
    public async Task RequestFactory_WithRetry()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://benchmark.invalid/");
        using var response = await _factory.SendAsync(request, CancellationToken.None);
    }

    private static HttpMessageInvoker CreateInvoker(
        Shield<HttpResponseMessage> shield,
        ShieldHttpHandlerOptions options,
        bool retry) =>
        new(new ShieldDelegatingHandler(shield, options)
        {
            InnerHandler = new BenchmarkHandler(retry),
        });

    private sealed class BenchmarkHandler(bool retry) : HttpMessageHandler
    {
        private int _attempt;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var status = retry && Interlocked.Increment(ref _attempt) % 2 == 1
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
