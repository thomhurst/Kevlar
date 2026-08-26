namespace Kevlar.Extensions.RateLimiting;

/// <summary>Configuration for a framework or delegate-backed rate-limiter adapter.</summary>
/// <remarks>
/// Rejections record the standard Kevlar rejection metric, then invoke and await
/// <see cref="OnRejected"/>. Callback failures are reported through
/// <see cref="KevlarDiagnostics.OnCallbackError"/> and do not replace the rejection.
/// </remarks>
public sealed class RateLimiterAdapterOptions
{
    /// <summary>An optional low-cardinality name used by strategy telemetry.</summary>
    public string? Name { get; set; }

    /// <summary>The number of permits acquired for each execution. Default 1.</summary>
    public int PermitCount { get; set; } = 1;

    /// <summary>
    /// Invoked and awaited when a lease rejects an execution. Return <see langword="default"/>
    /// from a synchronous callback.
    /// </summary>
    public Func<RateLimiterAdapterRejectedEvent, ValueTask>? OnRejected { get; set; }
}
