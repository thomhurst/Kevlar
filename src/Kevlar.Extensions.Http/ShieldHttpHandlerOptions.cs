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
    public long MaximumBufferSize { get; set; } = 1024 * 1024;

    /// <summary>
    /// Explicitly permits another attempt for methods other than GET, HEAD, OPTIONS, TRACE, PUT,
    /// and DELETE. A request factory also counts as explicit opt-in.
    /// </summary>
    public bool AllowUnsafeMethodReplay { get; set; }

    /// <summary>
    /// Optionally creates a fresh complete request for each zero-based attempt. Returned requests
    /// are owned and disposed by the handler; returning the original request preserves caller ownership.
    /// </summary>
    public Func<HttpRequestMessage, int, CancellationToken, ValueTask<HttpRequestMessage>>? RequestFactory { get; set; }

    /// <summary>Optional alternate-authority routing.</summary>
    public HttpEndpointRoutingOptions? Routing { get; set; }
}
