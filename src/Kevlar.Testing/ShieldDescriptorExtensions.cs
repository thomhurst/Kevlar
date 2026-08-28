using Kevlar.Strategies;

namespace Kevlar.Testing;

/// <summary>Creates structured, read-only descriptors for shields.</summary>
public static class ShieldDescriptorExtensions
{
    private const string RateLimiterAdapterStrategyTypeName =
        "Kevlar.Extensions.RateLimiting.RateLimiterStrategy";
    private const string RateLimiterAdapterAssemblyName = "Kevlar.Extensions.RateLimiting";

    /// <summary>Describes an untyped shield without executing it, excluding transparent decorators.</summary>
    public static ShieldDescriptor GetDescriptor(this Shield shield) =>
        GetDescriptor(shield, includeTransparent: false);

    /// <summary>Describes an untyped shield without executing it.</summary>
    /// <param name="shield">The shield to describe.</param>
    /// <param name="includeTransparent">
    /// Whether to include transparent infrastructure decorators such as structured logging.
    /// </param>
    public static ShieldDescriptor GetDescriptor(this Shield shield, bool includeTransparent)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        var snapshot = shield.CurrentSnapshot;
        return Create(
            snapshot.Name,
            resultType: null,
            snapshot.Time is not null,
            snapshot.Strategies,
            includeTransparent);
    }

    /// <summary>Describes a typed shield without executing it, excluding transparent decorators.</summary>
    public static ShieldDescriptor GetDescriptor<TResult>(this Shield<TResult> shield) =>
        GetDescriptor(shield, includeTransparent: false);

    /// <summary>Describes a typed shield without executing it.</summary>
    /// <param name="shield">The shield to describe.</param>
    /// <param name="includeTransparent">
    /// Whether to include transparent infrastructure decorators such as structured logging.
    /// </param>
    public static ShieldDescriptor GetDescriptor<TResult>(
        this Shield<TResult> shield,
        bool includeTransparent)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        var snapshot = shield.CurrentSnapshot;
        return Create(
            snapshot.Name,
            typeof(TResult),
            snapshot.Time is not null,
            snapshot.Strategies,
            includeTransparent);
    }

    private static ShieldDescriptor Create(
        string? name,
        Type? resultType,
        bool usesCustomTimeProvider,
        Strategy[] strategies,
        bool includeTransparent)
    {
        var descriptorCount = includeTransparent
            ? strategies.Length
            : strategies.Count(static strategy => strategy is not ITransparentStrategy);
        var descriptors = new StrategyDescriptor[descriptorCount];
        var descriptorIndex = 0;
        foreach (var strategy in strategies)
        {
            if (!includeTransparent && strategy is ITransparentStrategy)
            {
                continue;
            }

            descriptors[descriptorIndex++] = Describe(strategy);
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
        StrategyDescriptor descriptor = strategy switch
        {
            RetryStrategy retry => new RetryStrategyDescriptor(
                description,
                retry.MaxRetries,
                DescribeBackoff(retry.Backoff),
                retry.MaxDelay,
                retry.HasDelayGenerator,
                retry.HasNotification,
                retry.HasHandlingOverride),
            TimeoutStrategy timeout => new TimeoutStrategyDescriptor(
                description,
                timeout.Timeout,
                timeout.HasTimeoutGenerator,
                timeout.HasNotification),
            CircuitBreakerStrategy circuit => DescribeCircuitBreaker(description, circuit),
            RateLimitStrategy rateLimit => new RateLimitStrategyDescriptor(
                description,
                rateLimit.Permits,
                rateLimit.Window,
                rateLimit.Burst,
                rateLimit.QueueLimit,
                rateLimit.HasNotification),
            ConcurrencyLimitStrategy concurrency => new ConcurrencyLimitStrategyDescriptor(
                description,
                concurrency.MaxConcurrency,
                concurrency.QueueLimit,
                concurrency.HasNotification),
            HedgingStrategy hedging => new HedgeStrategyDescriptor(
                description,
                hedging.MaxHedgedAttempts,
                hedging.Delay,
                hedging.HasDelayGenerator,
                hedging.HasNotification,
                hedging.HasActionGenerator,
                hedging.HasHandlingOverride),
            IFallbackStrategyInspection fallback => new FallbackStrategyDescriptor(
                description,
                fallback.ResultType,
                fallback.HasNotification,
                strategy.HasHandlingOverride),
            _ when IsRateLimiterAdapterStrategy(strategy.GetType()) =>
                new CustomStrategyDescriptor(
                    StrategyKind.RateLimiterAdapter,
                    description,
                    strategy.GetType(),
                    handling: null),
            _ => new CustomStrategyDescriptor(
                description,
                strategy.GetType(),
                strategy.ReactiveJudge is { } judge ? new HandlingClause(judge) : null),
        };

        descriptor.SetHandlingClause(strategy.ReactiveJudge is { } handling
            ? new HandlingClauseDescriptor(handling.Description, handling.IsContextAware)
            : null);
        return descriptor;
    }

    private static bool IsRateLimiterAdapterStrategy(Type strategyType) =>
        string.Equals(strategyType.FullName, RateLimiterAdapterStrategyTypeName, StringComparison.Ordinal) &&
        string.Equals(
            strategyType.Assembly.GetName().Name,
            RateLimiterAdapterAssemblyName,
            StringComparison.Ordinal);

    private static CircuitBreakerStrategyDescriptor DescribeCircuitBreaker(
        string description,
        CircuitBreakerStrategy strategy) => new(
            description,
            strategy.Core.ConsecutiveFailures,
            strategy.Core.FailureRatio,
            strategy.Core.MinimumThroughput,
            strategy.Core.SamplingWindow,
            strategy.Core.BreakDuration,
            strategy.Core.HasMonitor,
            strategy.Core.HasNotification,
            strategy.HasHandlingOverride);

    private static BackoffDescriptor DescribeBackoff(Backoff backoff) => new(
        backoff.Kind,
        backoff.BaseDelay,
        backoff.Factor,
        backoff.MaxDelay,
        backoff.Jitter);
}
