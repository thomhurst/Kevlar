using Kevlar.Strategies;

namespace Kevlar.Testing;

/// <summary>Creates structured, read-only descriptors for shields.</summary>
public static class ShieldDescriptorExtensions
{
    /// <summary>Describes an untyped shield without executing it.</summary>
    public static ShieldDescriptor GetDescriptor(this Shield shield)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        return Create(shield.Name, resultType: null, shield.Time is not null, shield.Strategies);
    }

    /// <summary>Describes a typed shield without executing it.</summary>
    public static ShieldDescriptor GetDescriptor<TResult>(this Shield<TResult> shield)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        return Create(shield.Name, typeof(TResult), shield.Time is not null, shield.Strategies);
    }

    private static ShieldDescriptor Create(
        string? name,
        Type? resultType,
        bool usesCustomTimeProvider,
        Strategy[] strategies)
    {
        var descriptors = new StrategyDescriptor[strategies.Length];
        for (var index = 0; index < strategies.Length; index++)
        {
            descriptors[index] = Describe(strategies[index]);
        }

        return new ShieldDescriptor(
            name,
            resultType,
            usesCustomTimeProvider,
            Array.AsReadOnly(descriptors));
    }

    private static StrategyDescriptor Describe(Strategy strategy)
    {
        var description = strategy.Describe();
        return strategy switch
        {
            RetryStrategy retry => new RetryStrategyDescriptor(
                description,
                retry.MaxRetries,
                retry.Backoff,
                retry.MaxDelay,
                retry.HasDelayGenerator,
                retry.HasNotification),
            TimeoutStrategy timeout => new TimeoutStrategyDescriptor(
                description,
                timeout.Timeout,
                timeout.HasTimeoutGenerator,
                timeout.HasNotification),
            CircuitBreakerStrategy circuit => DescribeCircuitBreaker(description, circuit.Core),
            RateLimitStrategy rateLimit => new RateLimitStrategyDescriptor(
                description,
                rateLimit.Permits,
                rateLimit.Window,
                rateLimit.Burst,
                rateLimit.QueueLimit),
            ConcurrencyLimitStrategy concurrency => new ConcurrencyLimitStrategyDescriptor(
                description,
                concurrency.MaxConcurrency,
                concurrency.MaxQueue),
            HedgingStrategy hedging => new HedgingStrategyDescriptor(
                description,
                hedging.MaxAttempts,
                hedging.Delay,
                hedging.HasNotification),
            IFallbackStrategyInspection fallback => new FallbackStrategyDescriptor(
                description,
                fallback.ResultType,
                fallback.HasNotification),
            _ => new CustomStrategyDescriptor(description, strategy.GetType()),
        };
    }

    private static CircuitBreakerStrategyDescriptor DescribeCircuitBreaker(
        string description,
        CircuitBreakerCore core) => new(
            description,
            core.ConsecutiveFailures,
            core.FailureRatio,
            core.MinimumThroughput,
            core.SamplingWindow,
            core.BreakDuration,
            core.HasMonitor,
            core.HasNotification);
}
