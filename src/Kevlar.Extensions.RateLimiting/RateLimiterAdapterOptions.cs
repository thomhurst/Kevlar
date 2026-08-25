namespace Kevlar.Extensions.RateLimiting;

/// <summary>Configuration for a framework or delegate-backed rate-limiter adapter.</summary>
/// <remarks>
/// Rejections record the standard Kevlar rejection metric, invoke <see cref="OnRejected"/>, then
/// invoke and await <see cref="OnRejectedAsync"/>. A callback failure replaces the
/// <see cref="RateLimiterAdapterRejectedException"/> that would otherwise be returned.
/// </remarks>
public sealed class RateLimiterAdapterOptions
{
    /// <summary>The number of permits acquired for each execution. Default 1.</summary>
    public int PermitCount { get; set; } = 1;

    /// <summary>Invoked synchronously when a lease rejects an execution.</summary>
    public Action<RateLimiterAdapterRejectedEvent>? OnRejected { get; set; }

    /// <summary>Invoked and awaited after <see cref="OnRejected"/> for a rejected lease.</summary>
    public Func<RateLimiterAdapterRejectedEvent, ValueTask>? OnRejectedAsync { get; set; }
}
