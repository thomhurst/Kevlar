namespace Kevlar;

/// <summary>
/// Configuration for a token-bucket rate limit strategy: <see cref="Permits"/> executions per
/// <see cref="Window"/>, with bursts up to <see cref="Burst"/> and optional queueing.
/// </summary>
/// <remarks>
/// When an execution is rejected, rejection metrics are recorded, <see cref="OnRejected"/> runs,
/// and then <see cref="OnRejectedAsync"/> runs and is awaited. A callback failure replaces the
/// <see cref="RateLimitExceededException"/> that would otherwise be returned.
/// </remarks>
public sealed class RateLimitOptions
{
    /// <summary>Sustained number of executions allowed per <see cref="Window"/>. Default 100.</summary>
    public int Permits { get; set; } = 100;

    /// <summary>The time window over which <see cref="Permits"/> are replenished. Default 1 second.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum permits that can accumulate for bursting. Defaults to <see cref="Permits"/>.</summary>
    public int? Burst { get; set; }

    /// <summary>
    /// How many executions may wait for a permit instead of being rejected immediately.
    /// Waiting executions are delayed until their reserved permit is replenished. Default 0.
    /// </summary>
    public int QueueLimit { get; set; }

    /// <summary>Invoked synchronously when an execution is rejected.</summary>
    public Action<RateLimitRejectedEvent>? OnRejected { get; set; }

    /// <summary>Invoked and awaited after <see cref="OnRejected"/> when an execution is rejected.</summary>
    public Func<RateLimitRejectedEvent, ValueTask>? OnRejectedAsync { get; set; }
}
