namespace Kevlar.Extensions.Http;

/// <summary>Determines the per-request endpoint attempt order.</summary>
public enum HttpEndpointSelectionMode
{
    /// <summary>Use endpoints in their configured order.</summary>
    Ordered,

    /// <summary>
    /// Create a weighted order that is deterministic when
    /// <see cref="HttpEndpointRoutingOptions.Seed"/> is set.
    /// </summary>
    Weighted,
}
