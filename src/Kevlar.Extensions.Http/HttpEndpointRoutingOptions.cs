namespace Kevlar.Extensions.Http;

/// <summary>Configures alternate authorities for retry and hedge attempts.</summary>
/// <remarks>The endpoint list and scalar values are snapshotted with the handler options.</remarks>
public sealed class HttpEndpointRoutingOptions
{
    /// <summary>The available endpoint authorities. At least one is required when routing is enabled.</summary>
    public IList<HttpEndpoint> Endpoints { get; } = new List<HttpEndpoint>();

    /// <summary>The endpoint ordering algorithm.</summary>
    public HttpEndpointSelectionMode SelectionMode { get; set; }

    /// <summary>The deterministic seed used by weighted ordering.</summary>
    public int Seed { get; set; }

    /// <summary>
    /// Optionally creates a shield whose breaker or limiter state is isolated to one authority.
    /// The factory is called once per authority per handler.
    /// </summary>
    public Func<Uri, Shield<HttpResponseMessage>>? ShieldFactory { get; set; }
}
