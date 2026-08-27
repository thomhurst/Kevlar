using System.Globalization;
using Kevlar.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Kevlar.Extensions.DependencyInjection;

#pragma warning disable RS0026, RS0027 // Registration overload parity intentionally keeps optional arguments.

/// <summary>Registers Kevlar shields with the service collection.</summary>
public static class KevlarServiceCollectionExtensions
{
    private static readonly TimeSpan MaximumTimerDelay =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    /// <summary>Registers the <see cref="IKevlarRegistry"/>. Called automatically by the <c>AddShield</c> overloads.</summary>
    public static IServiceCollection AddKevlar(this IServiceCollection services)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        services.TryAddSingleton<KevlarRegistry>(sp =>
            new KevlarRegistry(sp, sp.GetServices<ShieldRegistration>()));
        services.TryAddSingleton<IKevlarRegistry>(sp => sp.GetRequiredService<KevlarRegistry>());
        return services;
    }

    /// <summary>Registers a named shield, resolvable via <see cref="IKevlarRegistry.GetShield(string)"/> or as a keyed <see cref="Shield"/> service.</summary>
    public static IServiceCollection AddShield(this IServiceCollection services, string name, Shield shield)
        => AddShield(services, name, shield, replace: false);

    /// <summary>Registers a named shield, optionally replacing any shield with the same name.</summary>
    public static IServiceCollection AddShield(
        this IServiceCollection services,
        string name,
        Shield shield,
        bool replace)
    {
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }
        return services.AddShield(name, _ => shield, replace);
    }

    /// <summary>Registers a named shield built from the service provider.</summary>
    public static IServiceCollection AddShield(this IServiceCollection services, string name, Func<IServiceProvider, Shield> factory)
        => AddShield(services, name, factory, replace: false);

    /// <summary>Registers a named shield built from the service provider, optionally replacing any shield with the same name.</summary>
    public static IServiceCollection AddShield(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, Shield> factory,
        bool replace)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (factory is null) { throw new ArgumentNullException(nameof(factory)); }

        PrepareRegistration(services, name, replace);
        services.AddKevlar();
        services.AddSingleton(new ShieldRegistration(
            name,
            null,
            serviceProvider => Decorate(serviceProvider, factory(serviceProvider), name)));
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
        => AddShield(services, name, configuration, replace: false);

    /// <summary>Registers a named shield from configuration, optionally replacing any shield with the same name.</summary>
    public static IServiceCollection AddShield(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        bool replace)
    {
        if (configuration is null) { throw new ArgumentNullException(nameof(configuration)); }

        return services.AddShield(name, _ =>
            BuildConfiguredShield(configuration).WithName(name), replace);
    }

    /// <summary>
    /// Registers a named, reload-aware shield bound from <paramref name="configuration"/>.
    /// A complete replacement is built on each change and atomically published through
    /// <see cref="IShieldProvider.Current"/>. Invalid replacements retain the last known-good
    /// snapshot and are reported to <paramref name="onReloadFailure"/> when supplied.
    /// </summary>
    /// <remarks>
    /// Changes are debounced for 250 milliseconds by default. Each successful replacement has
    /// fresh circuit-breaker, rate-limiter, and concurrency-limiter state. Resolve the keyed
    /// <see cref="IShieldProvider"/> to observe future replacements, or call
    /// <see cref="IKevlarRegistry.GetShield(string)"/> once per operation. Reloading names do not
    /// register a keyed <see cref="Shield"/>, preventing consumers from retaining a stale snapshot.
    /// Exceptions thrown by <paramref name="onReloadFailure"/> are suppressed so future reloads
    /// remain active.
    /// </remarks>
    public static IServiceCollection AddReloadingShield(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<Exception>? onReloadFailure = null)
        => AddReloadingShieldCore(
            services,
            name,
            configuration,
            new ReloadingShieldOptions(),
            onReloadFailure,
            replace: false);

    /// <summary>Registers a named reload-aware shield with failure reporting and explicit replacement.</summary>
    public static IServiceCollection AddReloadingShield(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<Exception>? onReloadFailure,
        bool replace) =>
        AddReloadingShieldCore(
            services,
            name,
            configuration,
            new ReloadingShieldOptions(),
            onReloadFailure,
            replace);

    /// <summary>Registers a named reload-aware shield with explicit reload options.</summary>
    public static IServiceCollection AddReloadingShield(
        this IServiceCollection services,
        string name,
        ReloadingShieldOptions options,
        IConfiguration configuration,
        Action<Exception>? onReloadFailure = null,
        bool replace = false) =>
        AddReloadingShieldCore(services, name, configuration, options, onReloadFailure, replace);

    private static IServiceCollection AddReloadingShieldCore(
        IServiceCollection services,
        string name,
        IConfiguration configuration,
        ReloadingShieldOptions options,
        Action<Exception>? onReloadFailure,
        bool replace)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (configuration is null) { throw new ArgumentNullException(nameof(configuration)); }
        if (options is null) { throw new ArgumentNullException(nameof(options)); }

        var (debounceDelay, timeProvider) = ValidateReloadingOptions(options);

        PrepareRegistration(services, name, replace);
        services.AddKevlar();
        services.AddKeyedSingleton<IShieldProvider>(
            name,
            (serviceProvider, _) => serviceProvider
                .GetRequiredService<KevlarRegistry>()
                .CreateReloadingProvider(() => new ReloadingShieldProvider(
                    () => Decorate(
                        serviceProvider,
                        BuildConfiguredShield(configuration).WithName(name),
                        name),
                    configuration.GetReloadToken,
                    onReloadFailure,
                    debounceDelay,
                    timeProvider)));
        services.AddSingleton(new ShieldRegistration(
            name,
            null,
            sp => sp.GetRequiredKeyedService<IShieldProvider>(name)));
        return services;
    }

    /// <summary>Registers a named result-aware shield, resolvable via <see cref="IKevlarRegistry.GetShield{TResult}(string)"/> or as a keyed <see cref="Shield{TResult}"/> service.</summary>
    public static IServiceCollection AddShield<TResult>(this IServiceCollection services, string name, Shield<TResult> shield)
        => AddShield(services, name, shield, replace: false);

    /// <summary>Registers a named result-aware shield, optionally replacing any shield with the same name.</summary>
    public static IServiceCollection AddShield<TResult>(
        this IServiceCollection services,
        string name,
        Shield<TResult> shield,
        bool replace)
    {
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }
        return services.AddShield(name, _ => shield, replace);
    }

    /// <summary>Registers a named result-aware shield built from the service provider.</summary>
    public static IServiceCollection AddShield<TResult>(this IServiceCollection services, string name, Func<IServiceProvider, Shield<TResult>> factory)
        => AddShield(services, name, factory, replace: false);

    /// <summary>Registers a named result-aware shield built from the service provider, optionally replacing any shield with the same name.</summary>
    public static IServiceCollection AddShield<TResult>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, Shield<TResult>> factory,
        bool replace)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (factory is null) { throw new ArgumentNullException(nameof(factory)); }

        PrepareRegistration(services, name, replace);
        services.AddKevlar();
        services.AddSingleton(new ShieldRegistration(
            name,
            typeof(TResult),
            serviceProvider => Decorate(serviceProvider, factory(serviceProvider), name)));
        services.AddKeyedSingleton(name, (sp, _) => sp.GetRequiredService<IKevlarRegistry>().GetShield<TResult>(name));
        services.AddKeyedSingleton<IShieldProvider<TResult>>(
            name,
            (sp, _) => new FixedShieldProvider<TResult>(
                sp.GetRequiredService<IKevlarRegistry>().GetShield<TResult>(name)));
        return services;
    }

    /// <summary>
    /// Registers a named result-aware shield bound from <paramref name="configuration"/>.
    /// The shield carries <paramref name="name"/> as its diagnostic name.
    /// </summary>
    public static IServiceCollection AddShield<TResult>(
        this IServiceCollection services,
        string name,
        IConfiguration configuration)
        => AddShield<TResult>(services, name, configuration, replace: false);

    /// <summary>Registers a named result-aware shield from configuration, optionally replacing any shield with the same name.</summary>
    public static IServiceCollection AddShield<TResult>(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        bool replace)
    {
        if (configuration is null) { throw new ArgumentNullException(nameof(configuration)); }

        return services.AddShield<TResult>(name, _ =>
            BuildConfiguredShield<TResult>(configuration).WithName(name), replace);
    }

    /// <summary>
    /// Registers a named, reload-aware result-aware shield bound from
    /// <paramref name="configuration"/>. Invalid replacements retain the last known-good
    /// snapshot and are reported to <paramref name="onReloadFailure"/> when supplied.
    /// </summary>
    /// <remarks>
    /// Changes are debounced for 250 milliseconds by default. Each successful replacement has
    /// fresh strategy state. Resolve the keyed
    /// <see cref="IShieldProvider{TResult}"/> to observe future replacements, or call
    /// <see cref="IKevlarRegistry.GetShield{TResult}(string)"/> once per operation. Reloading names
    /// do not register a keyed <see cref="Shield{TResult}"/>, preventing stale injection.
    /// Exceptions thrown by <paramref name="onReloadFailure"/> are suppressed.
    /// </remarks>
    public static IServiceCollection AddReloadingShield<TResult>(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<Exception>? onReloadFailure = null)
        => AddReloadingShieldCore<TResult>(
            services,
            name,
            configuration,
            new ReloadingShieldOptions(),
            onReloadFailure,
            replace: false);

    /// <summary>Registers a named reload-aware result-aware shield with failure reporting and explicit replacement.</summary>
    public static IServiceCollection AddReloadingShield<TResult>(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<Exception>? onReloadFailure,
        bool replace) =>
        AddReloadingShieldCore<TResult>(
            services,
            name,
            configuration,
            new ReloadingShieldOptions(),
            onReloadFailure,
            replace);

    /// <summary>Registers a named reload-aware result-aware shield with explicit reload options.</summary>
    public static IServiceCollection AddReloadingShield<TResult>(
        this IServiceCollection services,
        string name,
        ReloadingShieldOptions options,
        IConfiguration configuration,
        Action<Exception>? onReloadFailure = null,
        bool replace = false) =>
        AddReloadingShieldCore<TResult>(services, name, configuration, options, onReloadFailure, replace);

    private static IServiceCollection AddReloadingShieldCore<TResult>(
        IServiceCollection services,
        string name,
        IConfiguration configuration,
        ReloadingShieldOptions options,
        Action<Exception>? onReloadFailure,
        bool replace)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (configuration is null) { throw new ArgumentNullException(nameof(configuration)); }
        if (options is null) { throw new ArgumentNullException(nameof(options)); }

        var (debounceDelay, timeProvider) = ValidateReloadingOptions(options);

        PrepareRegistration(services, name, replace);
        services.AddKevlar();
        services.AddKeyedSingleton<IShieldProvider<TResult>>(
            name,
            (serviceProvider, _) => serviceProvider
                .GetRequiredService<KevlarRegistry>()
                .CreateReloadingProvider(() => new ReloadingShieldProvider<TResult>(
                    () => Decorate(
                        serviceProvider,
                        BuildConfiguredShield<TResult>(configuration).WithName(name),
                        name),
                    configuration.GetReloadToken,
                    onReloadFailure,
                    debounceDelay,
                    timeProvider)));
        services.AddSingleton(new ShieldRegistration(
            name,
            typeof(TResult),
            sp => sp.GetRequiredKeyedService<IShieldProvider<TResult>>(name)));
        return services;
    }

    /// <summary>
    /// Registers a named shield rebuilt from the corresponding named
    /// <see cref="IOptionsMonitor{TOptions}"/> value whenever it changes.
    /// </summary>
    public static IServiceCollection AddReloadingShield<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        this IServiceCollection services,
        string name,
        Func<TOptions, IServiceProvider, Shield> build,
        Action<Exception>? onReloadFailure = null)
        where TOptions : class
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (build is null) { throw new ArgumentNullException(nameof(build)); }

        PrepareRegistration(services, name, replace: false);
        services.AddKevlar();
        services.AddKeyedSingleton<IShieldProvider>(name, (serviceProvider, _) => serviceProvider
            .GetRequiredService<KevlarRegistry>()
            .CreateReloadingProvider(() => new OptionsReloadingShieldProvider<TOptions>(
                serviceProvider.GetRequiredService<IOptionsMonitor<TOptions>>(),
                name,
                options => Decorate(
                    serviceProvider,
                    build(options, serviceProvider).WithName(name),
                    name),
                onReloadFailure)));
        services.AddSingleton(new ShieldRegistration(
            name,
            null,
            serviceProvider => serviceProvider.GetRequiredKeyedService<IShieldProvider>(name)));
        return services;
    }

    /// <summary>
    /// Registers a named result-aware shield rebuilt from the corresponding named
    /// <see cref="IOptionsMonitor{TOptions}"/> value whenever it changes.
    /// </summary>
    public static IServiceCollection AddReloadingShield<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions,
        TResult>(
        this IServiceCollection services,
        string name,
        Func<TOptions, IServiceProvider, Shield<TResult>> build,
        Action<Exception>? onReloadFailure = null)
        where TOptions : class
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (build is null) { throw new ArgumentNullException(nameof(build)); }

        PrepareRegistration(services, name, replace: false);
        services.AddKevlar();
        services.AddKeyedSingleton<IShieldProvider<TResult>>(name, (serviceProvider, _) => serviceProvider
            .GetRequiredService<KevlarRegistry>()
            .CreateReloadingProvider(() => new OptionsReloadingShieldProvider<TOptions, TResult>(
                serviceProvider.GetRequiredService<IOptionsMonitor<TOptions>>(),
                name,
                options => Decorate(
                    serviceProvider,
                    build(options, serviceProvider).WithName(name),
                    name),
                onReloadFailure)));
        services.AddSingleton(new ShieldRegistration(
            name,
            typeof(TResult),
            serviceProvider => serviceProvider.GetRequiredKeyedService<IShieldProvider<TResult>>(name)));
        return services;
    }

    /// <summary>
    /// Registers a named, bounded untyped partition provider as a keyed singleton service.
    /// </summary>
    public static IServiceCollection AddPartitionedShield<TKey>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, TKey, Shield> factory,
        Action<PartitionedShieldOptions<TKey>>? configure = null,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (factory is null) { throw new ArgumentNullException(nameof(factory)); }

        var options = new PartitionedShieldOptions<TKey>();
        configure?.Invoke(options);
        services.AddKeyedSingleton<PartitionedShield<TKey>>(name, (serviceProvider, _) =>
            new PartitionedShield<TKey>(
                key => Decorate(serviceProvider, factory(serviceProvider, key), name),
                options,
                comparer));
        return services;
    }

    /// <summary>
    /// Registers a named, bounded result-aware partition provider as a keyed singleton service.
    /// </summary>
    public static IServiceCollection AddPartitionedShield<TKey, TResult>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, TKey, Shield<TResult>> factory,
        Action<PartitionedShieldOptions<TKey, TResult>>? configure = null,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (factory is null) { throw new ArgumentNullException(nameof(factory)); }

        var options = new PartitionedShieldOptions<TKey, TResult>();
        configure?.Invoke(options);
        services.AddKeyedSingleton<PartitionedShield<TKey, TResult>>(name, (serviceProvider, _) =>
            new PartitionedShield<TKey, TResult>(
                key => Decorate(serviceProvider, factory(serviceProvider, key), name),
                options,
                comparer));
        return services;
    }

    private static void PrepareRegistration(IServiceCollection services, string name, bool replace)
    {
        var duplicate = services.Any(descriptor =>
            !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(ShieldRegistration)
            && descriptor.ImplementationInstance is ShieldRegistration registration
            && string.Equals(registration.Name, name, StringComparison.Ordinal));
        if (!duplicate)
        {
            return;
        }

        if (!replace)
        {
            throw new InvalidOperationException($"A shield named '{name}' is already registered.");
        }

        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (IsRegistrationForName(descriptor, name)
                || IsKeyedShieldServiceForName(descriptor, name))
            {
                services.RemoveAt(index);
            }
        }
    }

    private static bool IsRegistrationForName(ServiceDescriptor descriptor, string name) =>
        !descriptor.IsKeyedService
        && descriptor.ServiceType == typeof(ShieldRegistration)
        && descriptor.ImplementationInstance is ShieldRegistration registration
        && string.Equals(registration.Name, name, StringComparison.Ordinal);

    private static bool IsKeyedShieldServiceForName(ServiceDescriptor descriptor, string name)
    {
        if (!descriptor.IsKeyedService
            || descriptor.ServiceKey is not string key
            || !string.Equals(key, name, StringComparison.Ordinal))
        {
            return false;
        }

        var serviceType = descriptor.ServiceType;
        if (serviceType == typeof(Shield) || serviceType == typeof(IShieldProvider))
        {
            return true;
        }

        if (!serviceType.IsGenericType)
        {
            return false;
        }

        var definition = serviceType.GetGenericTypeDefinition();
        return definition == typeof(Shield<>) || definition == typeof(IShieldProvider<>);
    }

    private static (TimeSpan DebounceDelay, TimeProvider TimeProvider) ValidateReloadingOptions(
        ReloadingShieldOptions options)
    {
        if (options.DebounceDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "DebounceDelay cannot be negative.");
        }

        if (options.DebounceDelay > MaximumTimerDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "DebounceDelay exceeds the runtime timer limit.");
        }

        if (options.TimeProvider is null)
        {
            throw new ArgumentException("TimeProvider cannot be null.", nameof(options));
        }

        return (options.DebounceDelay, options.TimeProvider);
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
            if (ReadBackoffKind(retry, nameof(RetryDefinition.Backoff)) is { } backoff)
            {
                retryDefinition.Backoff = backoff;
            }
            if (ReadDouble(retry, nameof(RetryDefinition.Factor)) is { } factor)
            {
                retryDefinition.Factor = factor;
            }
            if (ReadJitter(retry, nameof(RetryDefinition.Jitter)) is { } jitter)
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
            RejectLegacyQueueKey(concurrency);
            var concurrencyDefinition = new ConcurrencyLimitDefinition();
            if (ReadInt(concurrency, nameof(ConcurrencyLimitDefinition.MaxConcurrency)) is { } maxConcurrency)
            {
                concurrencyDefinition.MaxConcurrency = maxConcurrency;
            }
            if (ReadInt(concurrency, nameof(ConcurrencyLimitDefinition.QueueLimit)) is { } queueLimit)
            {
                concurrencyDefinition.QueueLimit = queueLimit;
            }

            definition.ConcurrencyLimit = concurrencyDefinition;
        }

        return definition;
    }

    private static Shield Decorate(
        IServiceProvider serviceProvider,
        Shield shield,
        string? name) =>
        ShieldDecoration.Apply(
            shield,
            name,
            serviceProvider.GetServices<IShieldDecorator>());

    private static Shield<TResult> Decorate<TResult>(
        IServiceProvider serviceProvider,
        Shield<TResult> shield,
        string? name) =>
        ShieldDecoration.Apply(
            shield,
            name,
            serviceProvider.GetServices<IShieldDecorator>());

    private static Shield BuildConfiguredShield(IConfiguration configuration)
    {
        try
        {
            return BindDefinition(configuration).Build();
        }
        catch (Exception exception) when (IsConfigurationFailure(exception))
        {
            throw AddConfigurationPath(configuration, exception);
        }
    }

    private static Shield<TResult> BuildConfiguredShield<TResult>(IConfiguration configuration)
    {
        try
        {
            return BindDefinition(configuration).Build<TResult>();
        }
        catch (Exception exception) when (IsConfigurationFailure(exception))
        {
            throw AddConfigurationPath(configuration, exception);
        }
    }

    private static KevlarConfigurationException AddConfigurationPath(
        IConfiguration configuration,
        Exception exception)
    {
        var path = configuration is IConfigurationSection section && !string.IsNullOrEmpty(section.Path)
            ? section.Path
            : "<root>";
        var configurationException = exception as KevlarConfigurationException
            ?? new KevlarConfigurationException(exception.Message, exception);
        return new KevlarConfigurationException(
            $"Configuration section '{path}' is invalid: {configurationException.Message}",
            configurationException);
    }

    private static bool IsConfigurationFailure(Exception exception) =>
        exception is KevlarConfigurationException or ArgumentException or InvalidOperationException;

    private static bool HasChildren(IConfigurationSection section) => section.GetChildren().Any();

    private static void RejectLegacyQueueKey(IConfigurationSection section)
    {
        const string legacyKey = "MaxQueue";
        if (section[legacyKey] is not null)
        {
            throw new InvalidOperationException(
                $"Configuration key '{ConfigurationPath.Combine(section.Path, legacyKey)}' is not supported; use 'QueueLimit'.");
        }
    }

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

    private static Jitter? ReadJitter(IConfiguration configuration, string key) =>
        Read(configuration, key) is { } value
            ? ParseJitter(configuration, key, value)
            : null;

    private static BackoffKind? ReadBackoffKind(IConfiguration configuration, string key)
    {
        if (Read(configuration, key) is not { } value)
        {
            return null;
        }

        var backoff = ParseEnum<BackoffKind>(configuration, key, value);
        return backoff != BackoffKind.Custom
            ? backoff
            : throw InvalidValue(
                configuration,
                key,
                value,
                "a configurable BackoffKind (None, Constant, Linear, or Exponential)");
    }

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

    private static TimeSpan ParseTimeSpan(IConfiguration configuration, string key, string value) =>
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw InvalidValue(configuration, key, value, "a TimeSpan");

    private static TEnum ParseEnum<TEnum>(IConfiguration configuration, string key, string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
        && Enum.IsDefined(typeof(TEnum), parsed)
            ? parsed
            : throw InvalidValue(configuration, key, value, $"a {typeof(TEnum).Name}");

    private static Jitter ParseJitter(IConfiguration configuration, string key, string value) =>
        bool.TryParse(value, out var enabled)
            ? enabled ? Jitter.Equal : Jitter.None
            : ParseEnum<Jitter>(configuration, key, value);

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

#pragma warning restore RS0026, RS0027
