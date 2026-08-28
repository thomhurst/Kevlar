namespace Kevlar;

/// <summary>
/// Configuration for a circuit breaker strategy. Configure either
/// <see cref="ConsecutiveFailures"/> (simple mode) or <see cref="FailureRatio"/>
/// (sampling mode). When neither is set, the breaker trips after 5 consecutive failures.
/// </summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>An optional low-cardinality name used by strategy telemetry.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Setting this — or, on <see cref="CircuitBreakerOptions{TResult}"/>, its <c>HandlesResult</c>
    /// — makes this circuit breaker ignore the ambient <c>When…</c> handling clause; this predicate
    /// then selects the exceptions it handles.
    /// </summary>
    /// <remarks>
    /// The ambient clause is started with <c>When…</c> on a shield and continued with <c>Or…</c> on
    /// the builder it returns, and applies to every reactive strategy chained after it. These
    /// properties replace that clause for this strategy alone; they do not narrow it.
    /// </remarks>
    /// <seealso cref="HandlingClause"/>
    public Func<Exception, bool>? HandlesException { get; set; }

    /// <summary>Locally handles exceptions using execution context and strategy metadata.</summary>
    public Func<HandlingEvent, bool>? HandlesExceptionContext { get; set; }

    internal bool HasHandlingOverride =>
        HandlesException is not null || HandlesExceptionContext is not null;

    /// <summary>Trips the circuit after this many consecutive handled failures.</summary>
    public int? ConsecutiveFailures { get; set; }

    /// <summary>
    /// Trips the circuit when the ratio of handled failures within <see cref="SamplingWindow"/>
    /// reaches this value (0 to 1), provided at least <see cref="MinimumThroughput"/> executions
    /// were observed in the window.
    /// </summary>
    public double? FailureRatio { get; set; }

    /// <summary>Minimum executions in the sampling window before <see cref="FailureRatio"/> can trip the circuit. Default 10.</summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>The rolling window over which the failure ratio is measured. Default 30 seconds.</summary>
    public TimeSpan SamplingWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the circuit stays open before allowing a probe. Default 15 seconds.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Produces the break duration when a handled outcome trips or re-opens the circuit, and is
    /// awaited before the circuit opens. The returned value must be positive. Return
    /// <c>new(duration)</c> from a synchronous generator. The event context is valid only until
    /// the returned task completes. When configured, this value overrides
    /// <see cref="BreakDuration"/>.
    /// </summary>
    public Func<CircuitBreakerBreakDurationEvent, ValueTask<TimeSpan>>? BreakDurationGenerator { get; set; }

    /// <summary>
    /// An optional monitor giving external code visibility of the circuit state plus manual
    /// <see cref="CircuitBreakerMonitor.Isolate"/> / <see cref="CircuitBreakerMonitor.Reset"/> control.
    /// A monitor can be bound to only one circuit breaker.
    /// </summary>
    public CircuitBreakerMonitor? Monitor { get; set; }

    /// <summary>
    /// Invoked and awaited on every state transition, before
    /// <see cref="CircuitBreakerMonitor.StateChanged"/>. Transitions are delivered serially
    /// outside the circuit lock, so a slow handler delays later transition publishers. Return
    /// <see langword="default"/> from a synchronous callback. The event context is valid only
    /// until the returned task completes. A non-reentrant publisher arriving during an active drain
    /// waits for the earlier handlers; an execution may therefore occupy a thread-pool thread until
    /// they return. A publisher reentered from <c>OnStateChanged</c> or <c>StateChanged</c> is queued
    /// and returns before the queued transition's observers run.
    /// </summary>
    public Func<CircuitBreakerStateChangedEvent, ValueTask>? OnStateChanged { get; set; }
}
