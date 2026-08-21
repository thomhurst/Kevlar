namespace Kevlar.Testing;

/// <summary>Read-only rate limiter configuration.</summary>
public sealed class RateLimitStrategyDescriptor : StrategyDescriptor
{
    internal RateLimitStrategyDescriptor(
        string description,
        int permits,
        TimeSpan window,
        int burst,
        int queueLimit)
        : base(StrategyKind.RateLimit, description)
    {
        Permits = permits;
        Window = window;
        Burst = burst;
        QueueLimit = queueLimit;
    }

    /// <summary>Permits replenished per window.</summary>
    public int Permits { get; }

    /// <summary>The replenishment window.</summary>
    public TimeSpan Window { get; }

    /// <summary>The maximum burst capacity.</summary>
    public int Burst { get; }

    /// <summary>The maximum wait queue size.</summary>
    public int QueueLimit { get; }
}
