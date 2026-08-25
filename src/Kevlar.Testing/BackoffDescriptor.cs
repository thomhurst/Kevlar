namespace Kevlar.Testing;

/// <summary>Immutable, callback-free description of retry backoff configuration.</summary>
public sealed class BackoffDescriptor
{
    internal BackoffDescriptor(
        BackoffKind kind,
        TimeSpan? baseDelay,
        double? factor,
        TimeSpan? maxDelay,
        Jitter? jitter)
    {
        Kind = kind;
        BaseDelay = baseDelay;
        Factor = factor;
        MaxDelay = maxDelay;
        Jitter = jitter;
    }

    /// <summary>The stable backoff category.</summary>
    public BackoffKind Kind { get; }

    /// <summary>The constant delay, linear step, or exponential initial delay, when applicable.</summary>
    public TimeSpan? BaseDelay { get; }

    /// <summary>The exponential multiplier, when applicable.</summary>
    public double? Factor { get; }

    /// <summary>The linear or exponential delay cap, when configured.</summary>
    public TimeSpan? MaxDelay { get; }

    /// <summary>The jitter mode for built-in backoffs, when applicable.</summary>
    public Jitter? Jitter { get; }
}
