namespace Kevlar;

/// <summary>
/// Configuration for a circuit breaker strategy. Configure either
/// <see cref="ConsecutiveFailures"/> (simple mode) or <see cref="FailureRatio"/>
/// (sampling mode). When neither is set, the breaker trips after 5 consecutive failures.
/// </summary>
public sealed class CircuitBreakerOptions
{
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
    public Func<HandlingEvent, bool>? HandlesExceptionWithContext { get; set; }

    internal bool HasHandlingOverride =>
        HandlesException is not null || HandlesExceptionWithContext is not null;

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
    /// Produces and awaits the break duration when a handled outcome trips or re-opens the
    /// circuit. The returned value must be positive. The event context is valid only until the
    /// callback completes. When configured, this value overrides <see cref="BreakDuration"/>.
    /// </summary>
    public Func<CircuitBreakerBreakDurationEvent, ValueTask<TimeSpan>>? BreakDurationGenerator { get; set; }

    /// <summary>
    /// An optional monitor giving external code visibility of the circuit state plus manual
    /// <see cref="CircuitBreakerMonitor.Isolate"/> / <see cref="CircuitBreakerMonitor.Reset"/> control.
    /// A monitor can be bound to only one circuit breaker.
    /// </summary>
    public CircuitBreakerMonitor? Monitor { get; set; }

    /// <summary>
    /// Invoked on every state transition, before <see cref="OnStateChangedAsync"/> and
    /// <see cref="CircuitBreakerMonitor.StateChanged"/>. Transitions are delivered serially
    /// outside the circuit lock. Exceptions propagate after all observers run; multiple failures
    /// are aggregated. The handler runs synchronously and blocks later transition publishers, so
    /// it should not perform I/O, wait on external work, or otherwise run for a long time.
    /// </summary>
    public Action<CircuitStateChangedEvent>? OnStateChanged { get; set; }

    /// <summary>
    /// Invoked and awaited on every state transition, after <see cref="OnStateChanged"/> and
    /// before <see cref="CircuitBreakerMonitor.StateChanged"/>. Transitions are delivered
    /// serially outside the circuit lock.
    /// </summary>
    public Func<CircuitStateChangedEvent, ValueTask>? OnStateChangedAsync { get; set; }
}
