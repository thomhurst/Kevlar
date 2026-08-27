namespace Kevlar;

/// <summary>Configures notifications for an untyped fallback strategy.</summary>
/// <remarks>
/// After a failure is selected for recovery, Kevlar records the fallback metric, invokes and
/// awaits <see cref="OnFallback"/>, and then runs the recovery action. Notification failures
/// are reported through <see cref="KevlarDiagnostics.OnCallbackError"/> and do not skip recovery.
/// Without an ambient clause or local handling override, fallback handles ordinary exceptions and
/// <see cref="ExecutionRejectedException"/> instances while letting cancellation and fatal runtime
/// exceptions propagate.
/// </remarks>
public sealed class FallbackOptions
{
    /// <summary>An optional low-cardinality name used by strategy telemetry.</summary>
    public string? Name { get; set; }

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

    /// <summary>
    /// Invoked and awaited before the recovery action. Return <see langword="default"/> from a
    /// synchronous callback.
    /// </summary>
    public Func<FallbackEvent, ValueTask>? OnFallback { get; set; }
}
