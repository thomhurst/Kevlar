namespace Kevlar;

/// <summary>Configures notifications for an untyped fallback strategy.</summary>
/// <remarks>
/// After a failure is selected for recovery, Kevlar records the fallback metric, invokes
/// <see cref="OnFallback"/>, awaits <see cref="OnFallbackAsync"/>, and then runs the recovery
/// action. A notification failure skips recovery and becomes the execution outcome.
/// </remarks>
public sealed class FallbackOptions
{
    /// <summary>
    /// Setting this makes this fallback ignore the ambient <c>When…</c> handling clause and handle
    /// only the exceptions this predicate selects.
    /// </summary>
    /// <remarks>
    /// The ambient clause is started with <c>When…</c> on a shield and continued with <c>Or…</c> on
    /// the builder it returns, and applies to every reactive strategy chained after it. This
    /// property replaces that clause for this strategy alone; it does not narrow it.
    /// </remarks>
    /// <seealso cref="HandlingClause"/>
    public Func<Exception, bool>? HandlesException { get; set; }

    /// <summary>Locally handles exceptions using execution context and strategy metadata.</summary>
    public Func<HandlingEvent, bool>? HandlesExceptionWithContext { get; set; }

    internal bool HasHandlingOverride =>
        HandlesException is not null || HandlesExceptionWithContext is not null;

    /// <summary>Invoked synchronously before the recovery action.</summary>
    public Action<FallbackEvent>? OnFallback { get; set; }

    /// <summary>Invoked and awaited after <see cref="OnFallback"/> and before the recovery action.</summary>
    public Func<FallbackEvent, ValueTask>? OnFallbackAsync { get; set; }
}
