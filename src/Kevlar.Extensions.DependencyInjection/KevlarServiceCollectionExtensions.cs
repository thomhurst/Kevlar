using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kevlar.Extensions.DependencyInjection;

/// <summary>Registers Kevlar shields with the service collection.</summary>
public static class KevlarServiceCollectionExtensions
{
    /// <summary>Registers the <see cref="IKevlarRegistry"/>. Called automatically by the <c>AddShield</c> overloads.</summary>
    public static IServiceCollection AddKevlar(this IServiceCollection services)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        services.TryAddSingleton<IKevlarRegistry>(sp => new KevlarRegistry(sp, sp.GetServices<ShieldRegistration>()));
        return services;
    }

    /// <summary>Registers a named shield, resolvable via <see cref="IKevlarRegistry.GetShield(string)"/> or as a keyed <see cref="Shield"/> service.</summary>
    public static IServiceCollection AddShield(this IServiceCollection services, string name, Shield shield)
    {
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }
        return services.AddShield(name, _ => shield);
    }

    /// <summary>Registers a named shield built from the service provider.</summary>
    public static IServiceCollection AddShield(this IServiceCollection services, string name, Func<IServiceProvider, Shield> factory)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (factory is null) { throw new ArgumentNullException(nameof(factory)); }

        services.AddKevlar();
        services.AddSingleton(new ShieldRegistration(name, null, factory));
        services.AddKeyedSingleton(name, (sp, _) => sp.GetRequiredService<IKevlarRegistry>().GetShield(name));
        return services;
    }

    /// <summary>
    /// Registers a named shield bound from <paramref name="configuration"/> (see
    /// <see cref="ShieldDefinition"/> for the schema), so its retry counts, timeouts and breaker
    /// thresholds are tunable without a redeploy. The shield carries <paramref name="name"/> as
    /// its diagnostic name.
    /// </summary>
    public static IServiceCollection AddShield(this IServiceCollection services, string name, IConfiguration configuration)
    {
        if (configuration is null) { throw new ArgumentNullException(nameof(configuration)); }

        return services.AddShield(name, _ =>
        {
            var definition = BindDefinition(configuration);
            return definition.Build().WithName(name);
        });
    }

    /// <summary>Registers a named result-aware shield, resolvable via <see cref="IKevlarRegistry.GetShield{TResult}(string)"/> or as a keyed <see cref="Shield{TResult}"/> service.</summary>
    public static IServiceCollection AddShield<TResult>(this IServiceCollection services, string name, Shield<TResult> shield)
    {
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }
        return services.AddShield(name, _ => shield);
    }

    /// <summary>Registers a named result-aware shield built from the service provider.</summary>
    public static IServiceCollection AddShield<TResult>(this IServiceCollection services, string name, Func<IServiceProvider, Shield<TResult>> factory)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (factory is null) { throw new ArgumentNullException(nameof(factory)); }

        services.AddKevlar();
        services.AddSingleton(new ShieldRegistration(name, typeof(TResult), factory));
        services.AddKeyedSingleton(name, (sp, _) => sp.GetRequiredService<IKevlarRegistry>().GetShield<TResult>(name));
        return services;
    }

    private static ShieldDefinition BindDefinition(IConfiguration configuration)
    {
        var definition = new ShieldDefinition
        {
            Timeout = ReadTimeSpan(configuration, nameof(ShieldDefinition.Timeout)),
            AttemptTimeout = ReadTimeSpan(configuration, nameof(ShieldDefinition.AttemptTimeout)),
        };

        var retry = configuration.GetSection(nameof(ShieldDefinition.Retry));
        if (HasValues(retry))
        {
            definition.Retry = new RetryDefinition
            {
                MaxRetries = ReadInt(retry, nameof(RetryDefinition.MaxRetries), 3),
                Backoff = ReadEnum(retry, nameof(RetryDefinition.Backoff), BackoffKind.Exponential),
                BaseDelay = ReadTimeSpan(retry, nameof(RetryDefinition.BaseDelay)),
                Factor = ReadDouble(retry, nameof(RetryDefinition.Factor), 2),
                Jitter = ReadBool(retry, nameof(RetryDefinition.Jitter), fallback: true),
                MaxDelay = ReadTimeSpan(retry, nameof(RetryDefinition.MaxDelay)),
            };
        }

        var breaker = configuration.GetSection(nameof(ShieldDefinition.CircuitBreaker));
        if (HasValues(breaker))
        {
            definition.CircuitBreaker = new CircuitBreakerDefinition
            {
                ConsecutiveFailures = ReadNullableInt(breaker, nameof(CircuitBreakerDefinition.ConsecutiveFailures)),
                FailureRatio = ReadNullableDouble(breaker, nameof(CircuitBreakerDefinition.FailureRatio)),
                MinimumThroughput = ReadInt(breaker, nameof(CircuitBreakerDefinition.MinimumThroughput), 10),
                SamplingWindow = ReadTimeSpan(breaker, nameof(CircuitBreakerDefinition.SamplingWindow)) ?? TimeSpan.FromSeconds(30),
                BreakDuration = ReadTimeSpan(breaker, nameof(CircuitBreakerDefinition.BreakDuration)) ?? TimeSpan.FromSeconds(15),
            };
        }

        var rateLimit = configuration.GetSection(nameof(ShieldDefinition.RateLimit));
        if (HasValues(rateLimit))
        {
            definition.RateLimit = new RateLimitDefinition
            {
                Permits = ReadInt(rateLimit, nameof(RateLimitDefinition.Permits), 100),
                Window = ReadTimeSpan(rateLimit, nameof(RateLimitDefinition.Window)) ?? TimeSpan.FromSeconds(1),
                Burst = ReadNullableInt(rateLimit, nameof(RateLimitDefinition.Burst)),
                QueueLimit = ReadInt(rateLimit, nameof(RateLimitDefinition.QueueLimit), 0),
            };
        }

        var concurrency = configuration.GetSection(nameof(ShieldDefinition.ConcurrencyLimit));
        if (HasValues(concurrency))
        {
            definition.ConcurrencyLimit = new ConcurrencyLimitDefinition
            {
                MaxConcurrency = ReadInt(concurrency, nameof(ConcurrencyLimitDefinition.MaxConcurrency), 10),
                MaxQueue = ReadInt(concurrency, nameof(ConcurrencyLimitDefinition.MaxQueue), 0),
            };
        }

        return definition;
    }

    private static bool HasValues(IConfigurationSection section) =>
        section.Value is not null || section.GetChildren().Any();

    private static string? Read(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : null;

    private static int ReadInt(IConfiguration configuration, string key, int fallback) =>
        Read(configuration, key) is { } value
            ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : fallback;

    private static int? ReadNullableInt(IConfiguration configuration, string key) =>
        Read(configuration, key) is { } value
            ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : null;

    private static double ReadDouble(IConfiguration configuration, string key, double fallback) =>
        Read(configuration, key) is { } value
            ? double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)
            : fallback;

    private static double? ReadNullableDouble(IConfiguration configuration, string key) =>
        Read(configuration, key) is { } value
            ? double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)
            : null;

    private static bool ReadBool(IConfiguration configuration, string key, bool fallback) =>
        Read(configuration, key) is { } value ? bool.Parse(value) : fallback;

    private static TimeSpan? ReadTimeSpan(IConfiguration configuration, string key) =>
        Read(configuration, key) is { } value
            ? TimeSpan.Parse(value, CultureInfo.InvariantCulture)
            : null;

    private static TEnum ReadEnum<TEnum>(IConfiguration configuration, string key, TEnum fallback)
        where TEnum : struct, Enum =>
        Read(configuration, key) is { } value
            ? Enum.Parse<TEnum>(value, ignoreCase: true)
            : fallback;
}
