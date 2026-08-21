namespace Kevlar;

/// <summary>
/// Names for Kevlar's built-in telemetry. On .NET 8+ targets every shield publishes metrics
/// through a <c>System.Diagnostics.Metrics.Meter</c> named <see cref="MeterName"/> with zero
/// configuration — subscribe with <c>AddMeter("Kevlar")</c> (OpenTelemetry) or a
/// <c>MeterListener</c>. On <c>netstandard2.0</c> the instruments are inert.
/// </summary>
/// <remarks>
/// The meter version is <c>1.0</c>. Instruments (all <c>Counter&lt;long&gt;</c>):
/// <list type="bullet">
/// <item><c>kevlar.executions</c> — completed public execution calls, including empty shields and
/// pre-cancelled calls; attributes <c>kevlar.shield.name</c>, <c>kevlar.execution.outcome</c> (<c>success</c>/<c>failure</c>)</item>
/// <item><c>kevlar.retries</c> — retry attempts; attribute <c>kevlar.shield.name</c></item>
/// <item><c>kevlar.timeouts</c> — executions cancelled by a timeout strategy; attribute <c>kevlar.shield.name</c></item>
/// <item><c>kevlar.hedges</c> — extra hedged attempts launched; attribute <c>kevlar.shield.name</c></item>
/// <item><c>kevlar.fallbacks</c> — outcomes replaced by a fallback; attribute <c>kevlar.shield.name</c></item>
/// <item><c>kevlar.rejections</c> — fail-fast rejections; attributes <c>kevlar.shield.name</c>, <c>kevlar.rejection.type</c> (<c>circuit_open</c>/<c>rate_limit</c>/<c>concurrency_limit</c>)</item>
/// <item><c>kevlar.circuit_breaker.transitions</c> — circuit state changes; attributes <c>kevlar.circuit_breaker.state.from</c>, <c>kevlar.circuit_breaker.state.to</c></item>
/// </list>
/// The <c>kevlar.shield.name</c> attribute is present only for shields named via <c>WithName</c>; an explicitly
/// empty name is emitted as an empty tag value.
/// </remarks>
public static class KevlarDiagnostics
{
    /// <summary>The name of Kevlar's <c>Meter</c>.</summary>
    public const string MeterName = "Kevlar";
}
