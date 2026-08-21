namespace Kevlar.Extensions.Http;

/// <summary>Determines the per-request endpoint attempt order.</summary>
public enum HttpEndpointSelectionMode
{
    /// <summary>Use endpoints in their configured order.</summary>
    Ordered,

    /// <summary>Create a deterministic weighted order from <see cref="HttpEndpointRoutingOptions.Seed"/>.</summary>
    Weighted,
}
