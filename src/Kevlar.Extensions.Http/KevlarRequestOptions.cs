namespace Kevlar.Extensions.Http;

/// <summary>Configures one HTTP request's Kevlar execution.</summary>
public sealed class KevlarRequestOptions
{
    /// <summary>Initializes the pooled execution properties before the selected shield runs.</summary>
    public Action<KevlarProperties>? ConfigureProperties { get; set; }

    /// <summary>Overrides the client's shield for this request.</summary>
    public Shield<HttpResponseMessage>? Shield { get; set; }

    /// <summary>Optional logical shield name available to per-request selectors.</summary>
    public string? ShieldName { get; set; }

    /// <summary>
    /// Overrides replay permission. <see langword="false"/> suppresses retries and hedges;
    /// <see langword="true"/> permits unsafe HTTP methods subject to content replay safety.
    /// </summary>
    public bool? AllowReplay { get; set; }

    /// <summary>An additional cancellation token linked with the token supplied to the handler.</summary>
    public CancellationToken CancellationToken { get; set; }
}
