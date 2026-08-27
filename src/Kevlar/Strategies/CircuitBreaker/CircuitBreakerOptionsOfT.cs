namespace Kevlar;

/// <summary>
/// Result-typed configuration for a circuit breaker strategy on a
/// <see cref="Shield{TResult}"/>.
/// </summary>
/// <remarks>
/// <see cref="CircuitBreakerOptions{TResult}"/> and <see cref="CircuitBreakerOptions"/> are
/// standalone sibling types with matching shared property names and defaults.
/// </remarks>
public sealed class CircuitBreakerOptions<TResult>
{
    /// <summary>An optional low-cardinality name used by strategy telemetry.</summary>
    public string? Name { get; set; }

    /// <inheritdoc cref="CircuitBreakerOptions.HandlesException"/>
    public Func<Exception, bool>? HandlesException { get; set; }

    /// <summary>Locally handles exceptions using the typed outcome and execution context.</summary>
    public Func<HandlingEvent<TResult>, bool>? HandlesExceptionContext { get; set; }

    /// <summary>
    /// Setting this — or <see cref="HandlesException"/> — makes this circuit
    /// breaker ignore the ambient <c>When…</c> handling clause; this predicate then selects the
    /// results it handles.
    /// </summary>
    /// <remarks>
    /// The ambient clause is started with <c>When…</c>/<c>WhenResult…</c> on a shield and continued
    /// with <c>Or…</c> on the builder it returns, and applies to every reactive strategy chained
    /// after it. These properties replace that clause for this strategy alone; they do not narrow it.
    /// </remarks>
    /// <seealso cref="HandlingClause"/>
    public Func<TResult, bool>? HandlesResult { get; set; }

    /// <summary>Locally handles results using the typed outcome and execution context.</summary>
    public Func<HandlingEvent<TResult>, bool>? HandlesResultContext { get; set; }

    internal bool HasHandlingOverride =>
        HandlesException is not null
        || HandlesResult is not null
        || HandlesExceptionContext is not null
        || HandlesResultContext is not null;

    /// <inheritdoc cref="CircuitBreakerOptions.ConsecutiveFailures"/>
    public int? ConsecutiveFailures { get; set; }

    /// <inheritdoc cref="CircuitBreakerOptions.FailureRatio"/>
    public double? FailureRatio { get; set; }

    /// <inheritdoc cref="CircuitBreakerOptions.MinimumThroughput"/>
    public int MinimumThroughput { get; set; } = 10;

    /// <inheritdoc cref="CircuitBreakerOptions.SamplingWindow"/>
    public TimeSpan SamplingWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc cref="CircuitBreakerOptions.BreakDuration"/>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(15);

    /// <inheritdoc cref="CircuitBreakerOptions.BreakDurationGenerator"/>
    public Func<CircuitBreakerBreakDurationEvent<TResult>, ValueTask<TimeSpan>>? BreakDurationGenerator { get; set; }

    /// <inheritdoc cref="CircuitBreakerOptions.Monitor"/>
    public CircuitBreakerMonitor? Monitor { get; set; }

    /// <inheritdoc cref="CircuitBreakerOptions.OnStateChanged"/>
    public Func<CircuitBreakerStateChangedEvent, ValueTask>? OnStateChanged { get; set; }

    internal CircuitBreakerOptions ToUntyped() => new()
    {
        Name = Name,
        HandlesException = HandlesException,
        ConsecutiveFailures = ConsecutiveFailures,
        FailureRatio = FailureRatio,
        MinimumThroughput = MinimumThroughput,
        SamplingWindow = SamplingWindow,
        BreakDuration = BreakDuration,
        Monitor = Monitor,
        OnStateChanged = OnStateChanged,
    };
}
