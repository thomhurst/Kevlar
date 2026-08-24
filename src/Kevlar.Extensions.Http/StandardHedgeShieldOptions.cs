namespace Kevlar.Extensions.Http;

/// <summary>Configures the standard endpoint-aware HTTP hedging pipeline.</summary>
public sealed class StandardHedgeShieldOptions
{
    /// <summary>The maximum duration of the complete request. Default 30 seconds.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Total attempts including the original. Default 2.</summary>
    public int MaxAttempts { get; set; } = 2;

    /// <summary>The delay before another hedged attempt starts. Default 1 second.</summary>
    public TimeSpan HedgeDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The maximum duration of each endpoint attempt. Default 10 seconds.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum concurrent attempts per endpoint. Default 10.</summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>Maximum attempts queued per endpoint. Default 0.</summary>
    public int QueueLimit { get; set; }

    /// <summary>Trips each endpoint circuit after this many consecutive transient failures.</summary>
    public int? ConsecutiveFailures { get; set; }

    /// <summary>
    /// Trips each endpoint circuit when its transient-failure ratio reaches this value.
    /// Default 0.5. Set to <see langword="null"/> when using <see cref="ConsecutiveFailures"/>.
    /// </summary>
    public double? FailureRatio { get; set; } = 0.5;

    /// <summary>Minimum endpoint attempts sampled before failure-ratio breaking. Default 10.</summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>The endpoint circuit sampling window. Default 30 seconds.</summary>
    public TimeSpan SamplingWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long an endpoint circuit remains open. Default 15 seconds.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>The endpoint ordering algorithm.</summary>
    public HttpEndpointSelectionMode SelectionMode { get; set; }

    /// <summary>The deterministic seed used by weighted endpoint ordering.</summary>
    public int Seed { get; set; }

    /// <summary>The endpoint authorities available to attempts. At least one is required.</summary>
    public IList<HttpEndpoint> Endpoints { get; } = new List<HttpEndpoint>();

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
}
