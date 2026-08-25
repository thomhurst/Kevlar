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
    /// <inheritdoc cref="HedgeOptions.HandlesException"/>
    public Func<Exception, bool>? HandlesException { get; set; }

    /// <summary>Locally handles exceptions using the typed outcome and execution context.</summary>
    public Func<HandlingEvent<TResult>, bool>? HandlesExceptionWithContext { get; set; }

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
    public Func<HandlingEvent<TResult>, bool>? HandlesResultWithContext { get; set; }

    internal bool HasHandlingOverride =>
        HandlesException is not null
        || HandlesResult is not null
        || HandlesExceptionWithContext is not null
        || HandlesResultWithContext is not null;

    /// <inheritdoc cref="HedgeOptions.MaxHedgedAttempts"/>
    public int MaxHedgedAttempts { get; set; } = 1;

    /// <inheritdoc cref="HedgeOptions.Delay"/>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);

    /// <inheritdoc cref="HedgeOptions.DelayGenerator"/>
    public Func<HedgeDelayEvent, TimeSpan>? DelayGenerator { get; set; }

    /// <inheritdoc cref="HedgeOptions.DelayGeneratorAsync"/>
    public Func<HedgeDelayEvent, ValueTask<TimeSpan>>? DelayGeneratorAsync { get; set; }

    /// <inheritdoc cref="HedgeOptions.OnHedge"/>
    public Action<HedgeEvent>? OnHedge { get; set; }

    /// <inheritdoc cref="HedgeOptions.OnHedgeAsync"/>
    public Func<HedgeEvent, ValueTask>? OnHedgeAsync { get; set; }

    /// <summary>
    /// Selects a replacement operation for each additional attempt. A <see langword="null"/>
    /// result runs the original operation.
    /// </summary>
    public Func<HedgeActionGeneratorEvent<TResult>, Func<CancellationToken, ValueTask<TResult>>?>?
        ActionGenerator { get; set; }

    internal HedgeOptions ToUntyped(HedgeActionGenerator? actionGenerator = null) => new()
    {
        HandlesException = HandlesException,
        MaxHedgedAttempts = MaxHedgedAttempts,
        Delay = Delay,
        DelayGenerator = DelayGenerator,
        DelayGeneratorAsync = DelayGeneratorAsync,
        OnHedge = OnHedge,
        OnHedgeAsync = OnHedgeAsync,
        ActionGenerator = actionGenerator,
    };
}
