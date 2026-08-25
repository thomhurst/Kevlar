namespace Kevlar;

/// <summary>
/// Configuration for a concurrency limit (concurrency limiter) strategy: at most
/// <see cref="MaxConcurrency"/> executions run at once, with up to <see cref="QueueLimit"/>
/// more waiting; anything beyond that is rejected immediately.
/// </summary>
/// <remarks>
/// When an execution is rejected, rejection metrics are recorded, <see cref="OnRejected"/> runs,
/// and then <see cref="OnRejectedAsync"/> runs and is awaited. Callback failures are reported
/// through <see cref="KevlarDiagnostics.OnCallbackError"/> and do not replace the rejection.
/// </remarks>
public sealed class ConcurrencyLimitOptions
{
    /// <summary>Maximum concurrent executions. Default 10.</summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>Maximum executions allowed to wait for a slot. Default 0 (reject immediately when full).</summary>
    public int QueueLimit { get; set; }

    /// <summary>Invoked synchronously when an execution is rejected.</summary>
    public Action<ConcurrencyLimitRejectedEvent>? OnRejected { get; set; }

    /// <summary>Invoked and awaited after <see cref="OnRejected"/> when an execution is rejected.</summary>
    public Func<ConcurrencyLimitRejectedEvent, ValueTask>? OnRejectedAsync { get; set; }
}
