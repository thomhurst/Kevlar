using System.Threading.RateLimiting;

namespace Kevlar.Extensions.RateLimiting;

#pragma warning disable RS0026 // Shield-family overload parity intentionally keeps configuration optional.

/// <summary>Adds System.Threading.RateLimiting-backed strategies to Kevlar shields.</summary>
public static class ShieldRateLimiterExtensions
{
    /// <summary>Appends a strategy backed by <paramref name="limiter"/>.</summary>
    public static Shield UseRateLimiter(
        this Shield shield,
        RateLimiter limiter,
        Action<RateLimiterAdapterOptions>? configure = null)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        return shield.Use(CreateStrategy(limiter, configure));
    }

    /// <summary>
    /// Appends a strategy backed by a context-aware <paramref name="limiter"/>.
    /// </summary>
    public static Shield UseRateLimiter(
        this Shield shield,
        PartitionedRateLimiter<KevlarContext> limiter,
        Action<RateLimiterAdapterOptions>? configure = null)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        return shield.Use(CreateStrategy(limiter, configure));
    }

    /// <summary>Appends a strategy backed by caller-provided asynchronous lease acquisition.</summary>
    public static Shield UseRateLimiter(
        this Shield shield,
        RateLimitLeaseAcquirer acquireLease,
        Action<RateLimiterAdapterOptions>? configure = null)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        return shield.Use(CreateStrategy(acquireLease, configure));
    }

    /// <summary>Appends a strategy backed by <paramref name="limiter"/>.</summary>
    public static Shield<TResult> UseRateLimiter<TResult>(
        this Shield<TResult> shield,
        RateLimiter limiter,
        Action<RateLimiterAdapterOptions>? configure = null)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        return shield.Use(CreateStrategy(limiter, configure));
    }

    /// <summary>
    /// Appends a strategy backed by a context-aware <paramref name="limiter"/>.
    /// </summary>
    public static Shield<TResult> UseRateLimiter<TResult>(
        this Shield<TResult> shield,
        PartitionedRateLimiter<KevlarContext> limiter,
        Action<RateLimiterAdapterOptions>? configure = null)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        return shield.Use(CreateStrategy(limiter, configure));
    }

    /// <summary>Appends a strategy backed by caller-provided asynchronous lease acquisition.</summary>
    public static Shield<TResult> UseRateLimiter<TResult>(
        this Shield<TResult> shield,
        RateLimitLeaseAcquirer acquireLease,
        Action<RateLimiterAdapterOptions>? configure = null)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        return shield.Use(CreateStrategy(acquireLease, configure));
    }

    private static Strategy CreateStrategy(
        RateLimiter limiter,
        Action<RateLimiterAdapterOptions>? configure)
    {
        if (limiter is null)
        {
            throw new ArgumentNullException(nameof(limiter));
        }

        var options = CreateOptions(configure);
        return new RateLimiterStrategy(
            (permitCount, context) => limiter.AcquireAsync(permitCount, context.CancellationToken),
            options,
            $"RateLimiter({GetLimiterName(limiter.GetType())})");
    }

    private static Strategy CreateStrategy(
        PartitionedRateLimiter<KevlarContext> limiter,
        Action<RateLimiterAdapterOptions>? configure)
    {
        if (limiter is null)
        {
            throw new ArgumentNullException(nameof(limiter));
        }

        var options = CreateOptions(configure);
        return new RateLimiterStrategy(
            (permitCount, context) => limiter.AcquireAsync(
                context,
                permitCount,
                context.CancellationToken),
            options,
            "RateLimiter(Partitioned)");
    }

    private static Strategy CreateStrategy(
        RateLimitLeaseAcquirer acquireLease,
        Action<RateLimiterAdapterOptions>? configure)
    {
        if (acquireLease is null)
        {
            throw new ArgumentNullException(nameof(acquireLease));
        }

        return new RateLimiterStrategy(
            acquireLease,
            CreateOptions(configure),
            "RateLimiter(Delegate)");
    }

    private static RateLimiterAdapterOptions CreateOptions(
        Action<RateLimiterAdapterOptions>? configure)
    {
        var options = new RateLimiterAdapterOptions();
        configure?.Invoke(options);
        return options;
    }

    private static string GetLimiterName(Type limiterType)
    {
        const string suffix = "RateLimiter";
        var name = limiterType.Name;
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - suffix.Length)
            : name;
    }
}

#pragma warning restore RS0026
