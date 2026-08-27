namespace Kevlar.Extensions.Http;

internal sealed class HttpShieldPipelineOptions
{
    private const int GoldenRatio = unchecked((int)0x9E3779B9u);
    private static int _nextUnseeded = unchecked((int)DateTime.UtcNow.Ticks);

    public HttpShieldPipelineOptions(ShieldHttpHandlerOptions source)
    {
        ContentReplayPolicy = source.ContentReplayPolicy;
        MaxBufferSize = source.MaxBufferSize;
        AllowUnsafeMethodReplay = source.AllowUnsafeMethodReplay;
        RequestFactory = source.RequestFactory;
        Routing = source.Routing is null ? null : new RoutingSnapshot(source.Routing);
    }

    public HttpContentReplayPolicy ContentReplayPolicy { get; }

    public long MaxBufferSize { get; }

    public bool AllowUnsafeMethodReplay { get; }

    public Func<HttpRequestMessage, int, CancellationToken, ValueTask<HttpRequestMessage>>? RequestFactory { get; }

    public RoutingSnapshot? Routing { get; }

    internal sealed class RoutingSnapshot
    {
        public RoutingSnapshot(HttpEndpointRoutingOptions source)
        {
            Endpoints = source.Endpoints.ToArray();
            SelectionMode = source.SelectionMode;
            Seed = source.Seed ?? Interlocked.Add(ref _nextUnseeded, GoldenRatio);
            ShieldFactory = source.ShieldFactory;
        }

        public HttpEndpoint[] Endpoints { get; }

        public HttpEndpointSelectionMode SelectionMode { get; }

        public int Seed { get; }

        public Func<Uri, Shield<HttpResponseMessage>>? ShieldFactory { get; }
    }
}
