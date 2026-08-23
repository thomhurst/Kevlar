namespace Kevlar.Extensions.DependencyInjection;

/// <summary>
/// A declarative shield that can be bound from configuration (appsettings, environment,
/// key vault…), so retry counts, timeouts and breaker thresholds are tunable without a redeploy.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Build"/> chains the sections in one fixed order, outermost first:
/// <see cref="Timeout"/> → <see cref="Retry"/> → <see cref="CircuitBreaker"/> →
/// <see cref="RateLimit"/> → <see cref="ConcurrencyLimit"/> → <see cref="AttemptTimeout"/>.
/// Only the sections present in configuration are added; the rest keep their relative order.
/// </para>
/// <para>
/// The order reads like any fluent chain: <see cref="Timeout"/> is the total budget around
/// everything, retries happen inside it, every attempt passes through the breaker and the two
/// limiters, and <see cref="AttemptTimeout"/> is the innermost per-attempt budget. Configuration
/// cannot reorder the chain — build the shield with the fluent API when a different shape is
/// needed.
/// </para>
/// </remarks>
public sealed class ShieldDefinition
{
    /// <summary>Total budget for the whole operation (outermost). Omit for none.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Retry configuration. Omit for none.</summary>
    public RetryDefinition? Retry { get; set; }

    /// <summary>Circuit breaker configuration. Omit for none.</summary>
    public CircuitBreakerDefinition? CircuitBreaker { get; set; }

    /// <summary>Rate limit configuration. Omit for none.</summary>
    public RateLimitDefinition? RateLimit { get; set; }

    /// <summary>Concurrency limit configuration. Omit for none.</summary>
    public ConcurrencyLimitDefinition? ConcurrencyLimit { get; set; }

    /// <summary>Budget for each individual attempt (innermost). Omit for none.</summary>
    public TimeSpan? AttemptTimeout { get; set; }

    /// <summary>
    /// Builds the configured <see cref="Shield"/>, chaining the declared sections outermost first:
    /// <see cref="Timeout"/> → <see cref="Retry"/> → <see cref="CircuitBreaker"/> →
    /// <see cref="RateLimit"/> → <see cref="ConcurrencyLimit"/> → <see cref="AttemptTimeout"/>.
    /// Sections left null are skipped without changing the order of the rest.
    /// </summary>
    public Shield Build()
    {
        var shield = Shield.Empty;

        if (Timeout is { } total)
        {
            shield = shield.Timeout(total);
        }

        if (Retry is { } retry)
        {
            shield = shield.Retry(options =>
            {
                options.MaxRetries = retry.MaxRetries;
                options.Backoff = retry.BuildBackoff();

                // Linear and exponential backoffs already carry the cap; setting it twice
                // would only duplicate it in the described pipeline.
                options.MaxDelay = retry.Backoff is BackoffKind.None or BackoffKind.Constant ? retry.MaxDelay : null;
            });
        }

        if (CircuitBreaker is { } breaker)
        {
            shield = shield.CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = breaker.ConsecutiveFailures;
                options.FailureRatio = breaker.FailureRatio;
                options.MinimumThroughput = breaker.MinimumThroughput;
                options.SamplingWindow = breaker.SamplingWindow;
                options.BreakDuration = breaker.BreakDuration;
            });
        }

        if (RateLimit is { } rate)
        {
            shield = shield.RateLimit(options =>
            {
                options.Permits = rate.Permits;
                options.Window = rate.Window;
                options.Burst = rate.Burst;
                options.QueueLimit = rate.QueueLimit;
            });
        }

        if (ConcurrencyLimit is { } concurrency)
        {
            shield = shield.ConcurrencyLimit(concurrency.MaxConcurrency, concurrency.MaxQueue);
        }

        if (AttemptTimeout is { } attempt)
        {
            shield = shield.Timeout(attempt);
        }

        return shield;
    }
}

/// <summary>The retry section of a <see cref="ShieldDefinition"/>.</summary>
public sealed class RetryDefinition
{
    /// <summary>Maximum retries after the initial attempt. Default 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>The backoff curve. Default <see cref="BackoffKind.Exponential"/>.</summary>
    public BackoffKind Backoff { get; set; } = BackoffKind.Exponential;

    /// <summary>
    /// The base delay: the constant delay, linear step, or exponential initial delay.
    /// Defaults per kind: 1s constant, 500ms linear, 250ms exponential.
    /// </summary>
    public TimeSpan? BaseDelay { get; set; }

    /// <summary>Exponential growth factor. Default 2.</summary>
    public double Factor { get; set; } = 2.0;

    /// <summary>Whether exponential delays are jittered. Default true.</summary>
    public bool Jitter { get; set; } = true;

    /// <summary>An absolute upper bound applied to every delay. Default 30s for exponential.</summary>
    public TimeSpan? MaxDelay { get; set; }

    internal Backoff BuildBackoff() => Backoff switch
    {
        BackoffKind.None => Kevlar.Backoff.None,
        BackoffKind.Constant => Kevlar.Backoff.Constant(BaseDelay ?? TimeSpan.FromSeconds(1)),
        BackoffKind.Linear => Kevlar.Backoff.Linear(BaseDelay ?? TimeSpan.FromMilliseconds(500), MaxDelay),
        BackoffKind.Exponential => Kevlar.Backoff.Exponential(
            BaseDelay ?? TimeSpan.FromMilliseconds(250),
            Factor,
            MaxDelay ?? TimeSpan.FromSeconds(30),
            Jitter),
        _ => throw new ArgumentOutOfRangeException(nameof(Backoff), Backoff, "Unknown backoff kind."),
    };
}

/// <summary>The backoff curves a <see cref="RetryDefinition"/> can declare.</summary>
public enum BackoffKind
{
    /// <summary>No delay between attempts.</summary>
    None,

    /// <summary>The same delay before every attempt.</summary>
    Constant,

    /// <summary>Linearly increasing delay.</summary>
    Linear,

    /// <summary>Exponentially increasing delay (the default).</summary>
    Exponential,
}

/// <summary>The circuit breaker section of a <see cref="ShieldDefinition"/>.</summary>
public sealed class CircuitBreakerDefinition
{
    /// <summary>Trip after this many consecutive failures (simple mode).</summary>
    public int? ConsecutiveFailures { get; set; }

    /// <summary>Trip at this failure ratio within the sampling window (sampling mode).</summary>
    public double? FailureRatio { get; set; }

    /// <summary>Minimum executions in the window before the ratio can trip. Default 10.</summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>The rolling ratio window. Default 30 seconds.</summary>
    public TimeSpan SamplingWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the circuit stays open. Default 15 seconds.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>The rate limit section of a <see cref="ShieldDefinition"/>.</summary>
public sealed class RateLimitDefinition
{
    /// <summary>Executions allowed per <see cref="Window"/>. Default 100.</summary>
    public int Permits { get; set; } = 100;

    /// <summary>The replenishment window. Default 1 second.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum accumulated burst. Defaults to <see cref="Permits"/>.</summary>
    public int? Burst { get; set; }

    /// <summary>Executions allowed to wait for a permit. Default 0.</summary>
    public int QueueLimit { get; set; }
}

/// <summary>The concurrency limit section of a <see cref="ShieldDefinition"/>.</summary>
public sealed class ConcurrencyLimitDefinition
{
    /// <summary>Maximum concurrent executions. Default 10.</summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>Executions allowed to wait for a slot. Default 0.</summary>
    public int MaxQueue { get; set; }
}
