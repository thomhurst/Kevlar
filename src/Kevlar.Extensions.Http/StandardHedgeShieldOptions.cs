namespace Kevlar.Extensions.Http;

/// <summary>Configures the standard HTTP hedging pipeline.</summary>
public sealed class StandardHedgeShieldOptions
{
    /// <summary>
    /// Configures the outer total timeout. Defaults to 30 seconds. Set
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to omit this stage.
    /// </summary>
    public TimeoutOptions TotalTimeout { get; set; } = new();

    /// <summary>Configures hedged attempts. Defaults to one additional attempt after one second.</summary>
    public HedgeOptions<HttpResponseMessage> Hedge { get; set; } = new();

    /// <summary>
    /// Optionally configures a concurrency limiter applied independently to each endpoint. The
    /// limiter is disabled when this property is <see langword="null"/>.
    /// </summary>
    public ConcurrencyLimitOptions? ConcurrencyLimit { get; set; }

    /// <summary>
    /// Configures the circuit breaker applied independently to each endpoint. Defaults to a 50%
    /// failure ratio over 30 seconds, with a minimum throughput of 10 and a 15-second break.
    /// </summary>
    public CircuitBreakerOptions<HttpResponseMessage> CircuitBreaker { get; set; } = new()
    {
        FailureRatio = 0.5,
    };

    /// <summary>
    /// Configures the timeout applied independently to each endpoint attempt. Defaults to 10
    /// seconds. Set <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to omit this stage.
    /// </summary>
    public TimeoutOptions AttemptTimeout { get; set; } = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    /// <summary>Configures request replay.</summary>
    public ShieldHttpHandlerOptions Handler { get; set; } = new();

    /// <summary>
    /// Optionally configures endpoint authorities and their ordering. When <see langword="null"/>,
    /// hedged attempts use the request's own authority in their natural order.
    /// </summary>
    public HttpEndpointRoutingOptions? Routing { get; set; }
}
