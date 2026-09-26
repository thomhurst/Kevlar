namespace Kevlar.Extensions.Http;

/// <summary>Configures alternate authorities for retry and hedge attempts.</summary>
/// <remarks>The endpoint list and scalar values are snapshotted with the handler options.</remarks>
public sealed class HttpEndpointRoutingOptions
{
    /// <summary>The static endpoint authorities, used when <see cref="EndpointProvider"/> is not set.</summary>
    public IList<HttpEndpoint> Endpoints { get; } = new List<HttpEndpoint>();

    /// <summary>Optionally resolves the endpoint authorities once for each request.</summary>
    /// <remarks>
    /// Replaces <see cref="Endpoints"/> when set. An empty result uses the request's own authority.
    /// The provider owns caching and must support concurrent requests. The returned list must remain
    /// stable while the handler snapshots its ordering. Resolution receives the linked request cancellation
    /// token and runs before the shield, outside its timeout budgets. A null list or null endpoint is invalid.
    /// </remarks>
    public Func<HttpRequestMessage, CancellationToken, ValueTask<IReadOnlyList<HttpEndpoint>>>? EndpointProvider { get; set; }

    /// <summary>The endpoint ordering algorithm.</summary>
    public HttpEndpointSelectionMode SelectionMode { get; set; }

    /// <summary>
    /// The optional deterministic seed used by weighted ordering. A <see langword="null"/> value
    /// selects a random initial order.
    /// </summary>
    public int? Seed { get; set; }

    /// <summary>
    /// Optionally creates a shield whose breaker or limiter state is isolated to one authority.
    /// The factory is called once per authority per handler.
    /// </summary>
    public Func<Uri, Shield<HttpResponseMessage>>? ShieldFactory { get; set; }
}
