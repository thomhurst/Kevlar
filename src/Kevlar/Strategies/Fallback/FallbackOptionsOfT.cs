namespace Kevlar;

/// <summary>
/// Configures result-typed notifications for a fallback strategy on a
/// <see cref="Shield{TResult}"/>.
/// </summary>
/// <remarks>
/// After an outcome is selected for recovery, Kevlar records the fallback metric, invokes
/// <see cref="OnFallback"/>, awaits <see cref="OnFallbackAsync"/>, and then runs the recovery
/// factory. A notification failure skips recovery and becomes the execution outcome.
/// </remarks>
public sealed class FallbackOptions<TResult>
{
    /// <summary>
    /// Setting this — or <see cref="HandlesResult"/> — makes this fallback ignore the ambient
    /// <c>When…</c> handling clause; this predicate then selects the exceptions it handles.
    /// </summary>
    /// <remarks>
    /// The ambient clause is started with <c>When…</c> on a shield and continued with <c>Or…</c> on
    /// the builder it returns, and applies to every reactive strategy chained after it. These
    /// properties replace that clause for this strategy alone; they do not narrow it.
    /// </remarks>
    /// <seealso cref="HandlingClause"/>
    public Func<Exception, bool>? HandlesException { get; set; }

    /// <summary>Locally handles exceptions using the typed outcome and execution context.</summary>
    public Func<HandlingEvent<TResult>, bool>? HandlesExceptionWithContext { get; set; }

    /// <summary>
    /// Setting this — or <see cref="HandlesException"/> — makes this fallback ignore the ambient
    /// <c>When…</c> handling clause; this predicate then selects the results it handles.
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

    /// <summary>Invoked synchronously before the recovery factory, with the typed handled outcome.</summary>
    public Action<FallbackEvent<TResult>>? OnFallback { get; set; }

    /// <summary>
    /// Invoked and awaited after <see cref="OnFallback"/> and before the recovery factory,
    /// with the typed handled outcome.
    /// </summary>
    public Func<FallbackEvent<TResult>, ValueTask>? OnFallbackAsync { get; set; }
}
