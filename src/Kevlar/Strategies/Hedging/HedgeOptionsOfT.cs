namespace Kevlar;

/// <summary>
/// Result-typed configuration for a hedging strategy on a <see cref="Shield{TResult}"/>.
/// </summary>
/// <remarks>
/// <see cref="HedgeOptions{TResult}"/> and <see cref="HedgeOptions"/> are standalone sibling types
/// with matching shared property names and defaults.
/// </remarks>
public sealed class HedgeOptions<TResult>
{
    /// <summary>An optional low-cardinality name used by strategy telemetry.</summary>
    public string? Name { get; set; }

    /// <inheritdoc cref="HedgeOptions.HandlesException"/>
    public Func<Exception, bool>? HandlesException { get; set; }

    /// <summary>Locally handles exceptions using the typed outcome and execution context.</summary>
    public Func<HandlingEvent<TResult>, bool>? HandlesExceptionContext { get; set; }

    /// <summary>
    /// Setting this — or <see cref="HandlesException"/> — makes this hedging strategy
    /// ignore the ambient <c>When…</c> handling clause; this predicate then selects the results it
    /// handles.
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

    /// <inheritdoc cref="HedgeOptions.MaxHedgedAttempts"/>
    public int MaxHedgedAttempts { get; set; } = 1;

    /// <inheritdoc cref="HedgeOptions.Delay"/>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);

    /// <inheritdoc cref="HedgeOptions.DelayGenerator"/>
    public Func<HedgeDelayEvent, ValueTask<TimeSpan>>? DelayGenerator { get; set; }

    /// <summary>
    /// Invoked and awaited before an additional hedged attempt starts, with the latest handled
    /// outcome when one triggered the hedge. Return <see langword="default"/> from a synchronous
    /// callback.
    /// </summary>
    public Func<HedgeEvent<TResult>, ValueTask>? OnHedge { get; set; }

    /// <summary>
    /// Selects a replacement operation for each additional attempt. A <see langword="null"/>
    /// result runs the original operation.
    /// </summary>
    public Func<HedgeActionGeneratorEvent<TResult>, Func<CancellationToken, ValueTask<TResult>>?>?
        ActionGenerator { get; set; }

    internal HedgeOptions ToUntyped() => new()
    {
        Name = Name,
        HandlesException = HandlesException,
        MaxHedgedAttempts = MaxHedgedAttempts,
        Delay = Delay,
        DelayGenerator = DelayGenerator,
    };
}
