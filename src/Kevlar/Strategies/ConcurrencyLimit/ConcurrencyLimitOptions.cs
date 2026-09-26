namespace Kevlar;

/// <summary>
/// Configuration for a concurrency limit (concurrency limiter) strategy: at most
/// <see cref="MaxConcurrency"/> executions run at once, with up to <see cref="QueueLimit"/>
/// more waiting; anything beyond that is rejected immediately.
/// </summary>
/// <remarks>
/// When an execution is rejected, rejection metrics are recorded and <see cref="OnRejected"/>
/// runs and is awaited. Callback failures are reported through
/// <see cref="KevlarDiagnostics.OnCallbackError"/> and do not replace the rejection.
/// </remarks>
public sealed class ConcurrencyLimitOptions
{
    /// <summary>An optional low-cardinality name used by strategy telemetry.</summary>
    public string? Name { get; set; }

    /// <summary>Maximum concurrent executions. Default 10.</summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>
    /// Maximum executions allowed to wait for a slot. Default 0 (reject immediately when full).
    /// Set QueueTimeout to bound queue residence independently of execution time.
    /// </summary>
    public int QueueLimit { get; set; }

    /// <summary>
    /// Maximum time spent waiting in the queue. Null (the default) leaves the wait unbounded.
    /// Must be positive and at most 4,294,967,294 milliseconds when configured.
    /// This limit ends when execution is admitted and does not bound execution time.
    /// </summary>
    public TimeSpan? QueueTimeout { get; set; }

    /// <summary>
    /// Invoked and awaited when an execution is rejected. Return <see langword="default"/> from a
    /// synchronous callback.
    /// </summary>
    public Func<ConcurrencyLimitRejectedEvent, ValueTask>? OnRejected { get; set; }
}
