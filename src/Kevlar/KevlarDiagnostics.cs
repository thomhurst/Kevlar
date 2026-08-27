using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// Names for Kevlar's built-in telemetry. On .NET 8+ targets every shield publishes counters and
/// duration metrics; the shipped state gauges require .NET 10 or later. Metrics are published
/// through a <c>System.Diagnostics.Metrics.Meter</c> named <see cref="MeterName"/> with zero
/// configuration — subscribe with <c>AddMeter("Kevlar")</c> (OpenTelemetry) or a
/// <c>MeterListener</c>. On <c>netstandard2.0</c> the instruments are inert.
/// </summary>
/// <remarks>
/// The meter version is <c>1.0</c>. Instruments:
/// <list type="bullet">
/// <item><c>kevlar.executions</c> — completed public execution calls, including empty shields and
/// pre-cancelled calls; attributes <c>kevlar.shield.name</c>, <c>kevlar.execution.outcome</c> (<c>success</c>/<c>failure</c>)</item>
/// <item><c>kevlar.retries</c> — retry attempts; attribute <c>kevlar.shield.name</c></item>
/// <item><c>kevlar.timeouts</c> — executions cancelled by a timeout strategy; attribute <c>kevlar.shield.name</c></item>
/// <item><c>kevlar.hedges</c> — extra hedged attempts launched; attribute <c>kevlar.shield.name</c></item>
/// <item><c>kevlar.fallbacks</c> — outcomes replaced by a fallback; attribute <c>kevlar.shield.name</c></item>
/// <item><c>kevlar.rejections</c> — fail-fast rejections; attributes <c>kevlar.shield.name</c>, <c>kevlar.rejection.type</c> (<c>circuit_open</c>/<c>rate_limit</c>/<c>rate_limiter_adapter</c>/<c>concurrency_limit</c>)</item>
/// <item><c>kevlar.circuit_breaker.transitions</c> — circuit state changes; attributes <c>kevlar.circuit_breaker.state.from</c>, <c>kevlar.circuit_breaker.state.to</c></item>
/// <item><c>kevlar.callback_errors</c> — exceptions thrown by callbacks or cleanup operations;
/// attributes <c>kevlar.shield.name</c>, <c>kevlar.callback.kind</c></item>
/// <item><c>kevlar.execution.duration</c> — execution duration histogram in seconds; attributes <c>kevlar.shield.name</c>, <c>kevlar.execution.outcome</c></item>
/// <item><c>kevlar.strategy.events</c> — strategy and custom events; attributes include shield,
/// strategy, event, severity, attempt, exception type, and an optional bounded operation key</item>
/// <item><c>kevlar.attempt.duration</c> — retry-attempt duration histogram in milliseconds with
/// the strategy-event attributes</item>
/// <item><c>kevlar.circuit_breaker.state</c> — current state gauge (<c>closed=0</c>, <c>open=1</c>, <c>half_open=2</c>, <c>isolated=3</c>)</item>
/// <item><c>kevlar.concurrency_limit.inflight</c>, <c>kevlar.concurrency_limit.queued</c>, and <c>kevlar.concurrency_limit.capacity</c> — concurrency-limit state gauges</item>
/// <item><c>kevlar.rate_limit.available</c> and <c>kevlar.rate_limit.queued</c> — rate-limit state gauges</item>
/// </list>
/// The <c>kevlar.shield.name</c> attribute is present only for shields named via <c>WithName</c>; an explicitly
/// empty name is emitted as an empty tag value. State gauges also carry the bounded
/// <c>kevlar.strategy.index</c> attribute so multiple stateful strategies in one pipeline remain distinct.
/// </remarks>
public static class KevlarDiagnostics
{
    /// <summary>The name of Kevlar's <c>Meter</c>.</summary>
    public const string MeterName = "Kevlar";

    /// <summary>
    /// Raised when a strategy notification, observer, or superseded-result disposal throws. Each
    /// subscriber is isolated: subscriber failures are swallowed and do not prevent later
    /// subscribers from running.
    /// </summary>
    public static event Action<CallbackErrorEvent>? OnCallbackError;

    /// <summary>
    /// Reports a callback or cleanup failure without allowing diagnostics subscribers to affect
    /// execution. Custom strategies can use this method to follow Kevlar's isolation contract.
    /// </summary>
    public static void ReportCallbackError(
        CallbackErrorKind kind,
        KevlarContext context,
        Exception exception)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        try
        {
            KevlarMetrics.CallbackError(context.ShieldName, kind);
        }
        catch
        {
            // Telemetry listeners are diagnostics too and cannot affect execution.
        }

        try
        {
            Internal.KevlarTelemetry.Record(
                context,
                strategyName: "Callback",
                eventName: "callback_error",
                KevlarTelemetrySeverity.Error,
                context.StrategyIndex,
                context.AttemptNumber,
                isSuccess: false,
                exception,
                callbackKind: kind);
        }
        catch
        {
            // Telemetry listeners are diagnostics too and cannot affect execution.
        }

        var callbackError = new CallbackErrorEvent(
            kind,
            context.ShieldName,
            context.StrategyIndex,
            exception);
        var handlers = OnCallbackError;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<CallbackErrorEvent> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(callbackError);
            }
            catch
            {
                // Diagnostic handlers cannot recursively become callback errors.
            }
        }
    }

    /// <summary>Subscribes a listener to synchronous strategy telemetry.</summary>
    /// <returns>A subscription that removes the listener when disposed.</returns>
    public static IDisposable Listen(IKevlarTelemetryListener listener)
    {
        Internal.Throw.IfNull(listener, nameof(listener));
        return Internal.KevlarTelemetry.Subscribe(listener);
    }
}
