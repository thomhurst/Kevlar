namespace Kevlar;

/// <summary>
/// Configuration for a bulkhead (concurrency limiter) strategy: at most
/// <see cref="MaxConcurrency"/> executions run at once, with up to <see cref="MaxQueue"/>
/// more waiting; anything beyond that is rejected immediately.
/// </summary>
public sealed class BulkheadOptions
{
    /// <summary>Maximum concurrent executions. Default 10.</summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>Maximum executions allowed to wait for a slot. Default 0 (reject immediately when full).</summary>
    public int MaxQueue { get; set; }
}
