namespace Kevlar;

/// <summary>
/// Configuration for a hedging strategy: launch up to <see cref="MaxHedgedAttempts"/> additional
/// attempts, staggered by <see cref="Delay"/>, and return the first acceptable outcome.
/// A handled failure always launches the next attempt immediately.
/// </summary>
/// <remarks>
/// The executed delegate may be invoked multiple times concurrently — it must be safe to do so.
/// Hedging requires asynchronous execution.
/// If no attempt produces an acceptable outcome, the final outcome processed by the coordinator
/// surfaces; selection among attempts already completed is not chronological.
/// Before an additional attempt starts, callbacks run in this order: <see cref="DelayGenerator"/>,
/// <see cref="OnHedge"/>, then <see cref="ActionGenerator"/>. Caller cancellation is checked
/// before callbacks and again before the generated operation starts.
/// </remarks>
public sealed class HedgeOptions
{
    /// <summary>An optional low-cardinality name used by strategy telemetry.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Setting this — or, on <see cref="HedgeOptions{TResult}"/>, its <c>HandlesResult</c> — makes
    /// this hedging strategy ignore the ambient <c>When…</c> handling clause; this predicate then
    /// selects the exceptions it handles.
    /// </summary>
    /// <remarks>
    /// The ambient clause is started with <c>When…</c> on a shield and continued with <c>Or…</c> on
    /// the builder it returns, and applies to every reactive strategy chained after it. These
    /// properties replace that clause for this strategy alone; they do not narrow it.
    /// </remarks>
    /// <seealso cref="HandlingClause"/>
    public Func<Exception, bool>? HandlesException { get; set; }

    /// <summary>Locally handles exceptions using execution context and attempt metadata.</summary>
    public Func<HandlingEvent, bool>? HandlesExceptionContext { get; set; }

    internal bool HasHandlingOverride =>
        HandlesException is not null || HandlesExceptionContext is not null;

    /// <summary>Maximum additional attempts after the original. Default 1.</summary>
    public int MaxHedgedAttempts { get; set; } = 1;

    /// <summary>
    /// Time to wait before launching the next attempt while the current ones are still running.
    /// <see cref="TimeSpan.Zero"/> removes timer staggering and starts scheduling the original and
    /// all additional attempts, even when the original completes synchronously. Callbacks and
    /// cancellation checks can still delay or prevent an additional delegate from starting. Any
    /// negative value hedges only on failure and is normalized to
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
    /// Default 1 second.
    /// </summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Selects the delay before each additional attempt while earlier attempts remain pending,
    /// and is awaited before the attempt is scheduled. The generated value replaces
    /// <see cref="Delay"/> for that attempt. Negative values are treated as
    /// <see cref="TimeSpan.Zero"/> and values above the runtime timer limit are clamped. Return
    /// <c>new(delay)</c> from a synchronous generator.
    /// </summary>
    public Func<HedgeDelayEvent, ValueTask<TimeSpan>>? DelayGenerator { get; set; }

    /// <summary>
    /// Invoked and awaited before an additional hedged attempt starts. Return
    /// <see langword="default"/> from a synchronous callback.
    /// </summary>
    public Func<HedgeEvent, ValueTask>? OnHedge { get; set; }

    /// <summary>
    /// Selects a replacement operation for each additional attempt. A <see langword="null"/>
    /// result runs the original operation.
    /// </summary>
    public Func<HedgeActionGeneratorEvent, Func<CancellationToken, ValueTask>?>?
        ActionGenerator { get; set; }
}

/// <summary>Describes a hedged attempt being launched.</summary>
public readonly struct HedgeEvent
{
    private readonly KevlarContext? _context;

    internal HedgeEvent(int attemptNumber, KevlarContext context)
    {
        AttemptNumber = attemptNumber;
        _context = context;
    }

    /// <summary>The zero-based execution attempt number (1 = first hedge after the initial attempt).</summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it after the hedge callback's
    /// returned task completes.
    /// </summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);
}
