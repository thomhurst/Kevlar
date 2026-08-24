namespace Kevlar.Extensions.Http;

/// <summary>Configures the standard HTTP resilience pipeline and its request handler.</summary>
public sealed class StandardHttpShieldOptions
{
    /// <summary>Configures the outer total timeout. Defaults to 30 seconds.</summary>
    public TimeoutOptions TotalTimeout { get; set; } = new();

    /// <summary>
    /// Configures transient retries. Defaults to three jittered exponential retries that honour
    /// <c>Retry-After</c> headers. Superseded responses are always disposed before notifications run.
    /// </summary>
    public RetryOptions<HttpResponseMessage> Retry { get; set; } = new()
    {
        DelayGenerator = HttpShield.RetryAfter,
        MaxDelay = TimeSpan.FromSeconds(10),
    };

    /// <summary>
    /// Configures the circuit breaker. Defaults to a 50% failure ratio over 30 seconds, with a
    /// minimum throughput of 10 and a 15-second break.
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new()
    {
        FailureRatio = 0.5,
    };

    /// <summary>
    /// Optionally configures a concurrency limiter between the circuit breaker and attempt
    /// timeout. The limiter is disabled when this property is <see langword="null"/>.
    /// </summary>
    public ConcurrencyLimitOptions? ConcurrencyLimit { get; set; }

    /// <summary>Configures the per-attempt timeout. Defaults to 10 seconds.</summary>
    public TimeoutOptions AttemptTimeout { get; set; } = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    /// <summary>Configures request replay and optional endpoint routing.</summary>
    public ShieldHttpHandlerOptions Handler { get; set; } = new();
}
