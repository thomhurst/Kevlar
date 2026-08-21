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
        services.AddKeyedSingleton<IShieldProvider>(
            name,
            (sp, _) => new FixedShieldProvider(sp.GetRequiredService<IKevlarRegistry>().GetShield(name)));
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

    /// <summary>
    /// Registers a named, reload-aware shield bound from <paramref name="configuration"/>.
    /// A complete replacement is built on each change and atomically published through
    /// <see cref="IShieldProvider.Current"/>. Invalid replacements retain the last known-good
    /// snapshot and are reported to <paramref name="onReloadFailure"/> when supplied.
    /// </summary>
    /// <remarks>
    /// Each successful replacement has fresh circuit-breaker, rate-limiter, and concurrency-
    /// limiter state. Resolve the keyed <see cref="IShieldProvider"/> to observe future replacements,
    /// or call <see cref="IKevlarRegistry.GetShield(string)"/> once per operation.
    /// A keyed <see cref="Shield"/> is an immutable snapshot and does not update after resolution.
    /// Exceptions thrown by <paramref name="onReloadFailure"/> are suppressed so future reloads
    /// remain active.
    /// </remarks>
    public static IServiceCollection AddReloadingShield(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<Exception>? onReloadFailure = null)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (configuration is null) { throw new ArgumentNullException(nameof(configuration)); }

        services.AddKevlar();
        services.AddKeyedSingleton<IShieldProvider>(
            name,
            (_, _) => new ReloadingShieldProvider(
                () => BindDefinition(configuration).Build().WithName(name),
                configuration.GetReloadToken,
                onReloadFailure));
        services.AddSingleton(new ShieldRegistration(
            name,
            null,
            sp => sp.GetRequiredKeyedService<IShieldProvider>(name)));
        services.AddKeyedSingleton(name, (sp, _) => sp.GetRequiredService<IKevlarRegistry>().GetShield(name));
        return services;
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
            Timeout = ReadNullableTimeSpan(configuration, nameof(ShieldDefinition.Timeout)),
            AttemptTimeout = ReadNullableTimeSpan(configuration, nameof(ShieldDefinition.AttemptTimeout)),
        };

        var retry = configuration.GetSection(nameof(ShieldDefinition.Retry));
        if (HasChildren(retry))
        {
            var retryDefinition = new RetryDefinition
            {
                BaseDelay = ReadNullableTimeSpan(retry, nameof(RetryDefinition.BaseDelay)),
                MaxDelay = ReadNullableTimeSpan(retry, nameof(RetryDefinition.MaxDelay)),
            };
            if (ReadInt(retry, nameof(RetryDefinition.MaxRetries)) is { } maxRetries)
            {
                retryDefinition.MaxRetries = maxRetries;
            }
            if (ReadEnum<BackoffKind>(retry, nameof(RetryDefinition.Backoff)) is { } backoff)
            {
                retryDefinition.Backoff = backoff;
            }
            if (ReadDouble(retry, nameof(RetryDefinition.Factor)) is { } factor)
            {
                retryDefinition.Factor = factor;
            }
            if (ReadBool(retry, nameof(RetryDefinition.Jitter)) is { } jitter)
            {
                retryDefinition.Jitter = jitter;
            }

            definition.Retry = retryDefinition;
        }

        var breaker = configuration.GetSection(nameof(ShieldDefinition.CircuitBreaker));
        if (HasChildren(breaker))
        {
            var breakerDefinition = new CircuitBreakerDefinition
            {
                ConsecutiveFailures = ReadNullableInt(breaker, nameof(CircuitBreakerDefinition.ConsecutiveFailures)),
                FailureRatio = ReadNullableDouble(breaker, nameof(CircuitBreakerDefinition.FailureRatio)),
            };
            if (ReadInt(breaker, nameof(CircuitBreakerDefinition.MinimumThroughput)) is { } minimumThroughput)
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
        if (HasChildren(rateLimit))
        {
            var rateLimitDefinition = new RateLimitDefinition
            {
                Burst = ReadNullableInt(rateLimit, nameof(RateLimitDefinition.Burst)),
            };
            if (ReadInt(rateLimit, nameof(RateLimitDefinition.Permits)) is { } permits)
            {
                rateLimitDefinition.Permits = permits;
            }
            if (ReadTimeSpan(rateLimit, nameof(RateLimitDefinition.Window)) is { } window)
            {
                rateLimitDefinition.Window = window;
            }
            if (ReadInt(rateLimit, nameof(RateLimitDefinition.QueueLimit)) is { } queueLimit)
            {
                rateLimitDefinition.QueueLimit = queueLimit;
            }

            definition.RateLimit = rateLimitDefinition;
        }

        var concurrency = configuration.GetSection(nameof(ShieldDefinition.ConcurrencyLimit));
        if (HasChildren(concurrency))
        {
            var concurrencyDefinition = new ConcurrencyLimitDefinition();
            if (ReadInt(concurrency, nameof(ConcurrencyLimitDefinition.MaxConcurrency)) is { } maxConcurrency)
            {
                concurrencyDefinition.MaxConcurrency = maxConcurrency;
            }
            if (ReadInt(concurrency, nameof(ConcurrencyLimitDefinition.MaxQueue)) is { } maxQueue)
            {
                concurrencyDefinition.MaxQueue = maxQueue;
            }

            definition.ConcurrencyLimit = concurrencyDefinition;
        }

        return definition;
    }

    private static bool HasChildren(IConfigurationSection section) => section.GetChildren().Any();

    private static string? Read(IConfiguration configuration, string key) =>
        configuration[key];

    private static int? ReadInt(IConfiguration configuration, string key) =>
        Read(configuration, key) is { } value
            ? ParseInt(configuration, key, value)
            : null;

    private static int? ReadNullableInt(IConfiguration configuration, string key) =>
        ReadNullable(configuration, key) is { } value
            ? ParseInt(configuration, key, value)
            : null;

    private static double? ReadDouble(IConfiguration configuration, string key) =>
        Read(configuration, key) is { } value
            ? ParseDouble(configuration, key, value)
            : null;

    private static double? ReadNullableDouble(IConfiguration configuration, string key) =>
        ReadNullable(configuration, key) is { } value
            ? ParseDouble(configuration, key, value)
            : null;

    private static bool? ReadBool(IConfiguration configuration, string key) =>
        Read(configuration, key) is { } value ? ParseBool(configuration, key, value) : null;

    private static TimeSpan? ReadTimeSpan(IConfiguration configuration, string key) =>
        Read(configuration, key) is { } value
            ? ParseTimeSpan(configuration, key, value)
            : null;

    private static TimeSpan? ReadNullableTimeSpan(IConfiguration configuration, string key) =>
        ReadNullable(configuration, key) is { } value
            ? ParseTimeSpan(configuration, key, value)
            : null;

    private static TEnum? ReadEnum<TEnum>(IConfiguration configuration, string key)
        where TEnum : struct, Enum =>
        Read(configuration, key) is { } value
            ? ParseEnum<TEnum>(configuration, key, value)
            : null;

    private static string? ReadNullable(IConfiguration configuration, string key)
    {
        var value = Read(configuration, key);
        return string.IsNullOrEmpty(value) ? null : value;
    }

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
