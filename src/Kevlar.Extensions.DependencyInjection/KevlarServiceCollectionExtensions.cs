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
            var retryDefinition = new RetryDefinition
            {
                BaseDelay = ReadTimeSpan(retry, nameof(RetryDefinition.BaseDelay)),
                MaxDelay = ReadTimeSpan(retry, nameof(RetryDefinition.MaxDelay)),
            };
            if (ReadNullableInt(retry, nameof(RetryDefinition.MaxRetries)) is { } maxRetries)
            {
                retryDefinition.MaxRetries = maxRetries;
            }
            if (ReadNullableEnum<BackoffKind>(retry, nameof(RetryDefinition.Backoff)) is { } backoff)
            {
                retryDefinition.Backoff = backoff;
            }
            if (ReadNullableDouble(retry, nameof(RetryDefinition.Factor)) is { } factor)
            {
                retryDefinition.Factor = factor;
            }
            if (ReadNullableBool(retry, nameof(RetryDefinition.Jitter)) is { } jitter)
            {
                retryDefinition.Jitter = jitter;
            }

            definition.Retry = retryDefinition;
        }

        var breaker = configuration.GetSection(nameof(ShieldDefinition.CircuitBreaker));
        if (HasValues(breaker))
        {
            var breakerDefinition = new CircuitBreakerDefinition
            {
                ConsecutiveFailures = ReadNullableInt(breaker, nameof(CircuitBreakerDefinition.ConsecutiveFailures)),
                FailureRatio = ReadNullableDouble(breaker, nameof(CircuitBreakerDefinition.FailureRatio)),
            };
            if (ReadNullableInt(breaker, nameof(CircuitBreakerDefinition.MinimumThroughput)) is { } minimumThroughput)
            {
                breakerDefinition.MinimumThroughput = minimumThroughput;
            }
            if (ReadTimeSpan(breaker, nameof(CircuitBreakerDefinition.SamplingWindow)) is { } samplingWindow)
            {
                breakerDefinition.SamplingWindow = samplingWindow;
            }
            if (ReadTimeSpan(breaker, nameof(CircuitBreakerDefinition.BreakDuration)) is { } breakDuration)
            {
                breakerDefinition.BreakDuration = breakDuration;
            }

            definition.CircuitBreaker = breakerDefinition;
        }

        var rateLimit = configuration.GetSection(nameof(ShieldDefinition.RateLimit));
        if (HasValues(rateLimit))
        {
            var rateLimitDefinition = new RateLimitDefinition
            {
                Burst = ReadNullableInt(rateLimit, nameof(RateLimitDefinition.Burst)),
            };
            if (ReadNullableInt(rateLimit, nameof(RateLimitDefinition.Permits)) is { } permits)
            {
                rateLimitDefinition.Permits = permits;
            }
            if (ReadTimeSpan(rateLimit, nameof(RateLimitDefinition.Window)) is { } window)
            {
                rateLimitDefinition.Window = window;
            }
            if (ReadNullableInt(rateLimit, nameof(RateLimitDefinition.QueueLimit)) is { } queueLimit)
            {
                rateLimitDefinition.QueueLimit = queueLimit;
            }

            definition.RateLimit = rateLimitDefinition;
        }

        var concurrency = configuration.GetSection(nameof(ShieldDefinition.ConcurrencyLimit));
        if (HasValues(concurrency))
        {
            var concurrencyDefinition = new ConcurrencyLimitDefinition();
            if (ReadNullableInt(concurrency, nameof(ConcurrencyLimitDefinition.MaxConcurrency)) is { } maxConcurrency)
            {
                concurrencyDefinition.MaxConcurrency = maxConcurrency;
            }
            if (ReadNullableInt(concurrency, nameof(ConcurrencyLimitDefinition.MaxQueue)) is { } maxQueue)
            {
                concurrencyDefinition.MaxQueue = maxQueue;
            }

            definition.ConcurrencyLimit = concurrencyDefinition;
        }

        return definition;
    }

    private static bool HasValues(IConfigurationSection section) =>
        section.Value is not null || section.GetChildren().Any();

    private static string? Read(IConfiguration configuration, string key) =>
        configuration[key];

    private static int? ReadNullableInt(IConfiguration configuration, string key) =>
        Read(configuration, key) is { } value
            ? ParseInt(configuration, key, value)
            : null;

    private static double? ReadNullableDouble(IConfiguration configuration, string key) =>
        Read(configuration, key) is { } value
            ? ParseDouble(configuration, key, value)
            : null;

    private static bool? ReadNullableBool(IConfiguration configuration, string key) =>
        Read(configuration, key) is { } value ? ParseBool(configuration, key, value) : null;

    private static TimeSpan? ReadTimeSpan(IConfiguration configuration, string key) =>
        Read(configuration, key) is { } value
            ? ParseTimeSpan(configuration, key, value)
            : null;

    private static TEnum? ReadNullableEnum<TEnum>(IConfiguration configuration, string key)
        where TEnum : struct, Enum =>
        Read(configuration, key) is { } value
            ? ParseEnum<TEnum>(configuration, key, value)
            : null;

    private static int ParseInt(IConfiguration configuration, string key, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw InvalidValue(configuration, key, value, "an integer");

    private static double ParseDouble(IConfiguration configuration, string key, string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw InvalidValue(configuration, key, value, "a number");

    private static bool ParseBool(IConfiguration configuration, string key, string value) =>
        bool.TryParse(value, out var parsed)
            ? parsed
            : throw InvalidValue(configuration, key, value, "a Boolean");

    private static TimeSpan ParseTimeSpan(IConfiguration configuration, string key, string value) =>
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw InvalidValue(configuration, key, value, "a TimeSpan");

    private static TEnum ParseEnum<TEnum>(IConfiguration configuration, string key, string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw InvalidValue(configuration, key, value, $"a {typeof(TEnum).Name}");

    private static InvalidOperationException InvalidValue(
        IConfiguration configuration,
        string key,
        string value,
        string expected)
    {
        var path = configuration is IConfigurationSection { Path.Length: > 0 } section
            ? ConfigurationPath.Combine(section.Path, key)
            : key;
        return new InvalidOperationException(
            $"Configuration value '{value}' for '{path}' is not {expected}.");
    }
}
