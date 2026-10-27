namespace Kevlar.Extensions.Http;

/// <summary>Configures safe request replay and optional endpoint routing.</summary>
/// <remarks>
/// Values are snapshotted when the handler pipeline is registered or created. Later mutations do
/// not reconfigure that pipeline; use a configuration-backed reloading registration for updates.
/// </remarks>
public sealed class ShieldHttpHandlerOptions
{
    /// <summary>The request-content replay policy. The default never buffers caller content.</summary>
    public HttpContentReplayPolicy ContentReplayPolicy { get; set; }

    /// <summary>The maximum buffered request-content size. Defaults to 1 MiB.</summary>
    public long MaxBufferSize { get; set; } = 1024 * 1024;

    /// <summary>
    /// Explicitly permits another attempt for methods rejected by <see cref="IsReplaySafeMethod"/>.
    /// A request factory also counts as explicit opt-in. Content must still be replayable.
    /// </summary>
    public bool AllowUnsafeMethodReplay { get; set; }

    /// <summary>
    /// Optionally determines whether an HTTP method permits additional attempts. A null value
    /// uses GET, HEAD, OPTIONS, TRACE, PUT, and DELETE, the RFC 9110 idempotent methods.
    /// </summary>
    /// <remarks>
    /// The predicate may run concurrently and must be thread-safe. Explicit replay opt-ins bypass
    /// it; per-request <see cref="KevlarHttpRequestExtensions.DisableReplay"/> always takes precedence.
    /// Content must still be replayable when the predicate returns true.
    /// </remarks>
    public Func<HttpMethod, bool>? IsReplaySafeMethod { get; set; }

    /// <summary>
    /// Optionally creates a fresh complete request for each zero-based attempt. Returned requests
    /// are owned and disposed by the handler; returning the original request preserves caller ownership.
    /// </summary>
    public Func<HttpRequestMessage, int, CancellationToken, ValueTask<HttpRequestMessage>>? RequestFactory { get; set; }

    /// <summary>Optional alternate-authority routing.</summary>
    public HttpEndpointRoutingOptions? Routing { get; set; }
}
