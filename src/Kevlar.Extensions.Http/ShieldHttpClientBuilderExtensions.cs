using Kevlar;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Http;
using Kevlar.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

#pragma warning disable RS0026 // Named/configured HttpClient overload parity intentionally keeps optional arguments.

/// <summary>Adds Kevlar resilience to <see cref="IHttpClientBuilder"/> pipelines.</summary>
public static class ShieldHttpClientBuilderExtensions
{
    /// <summary>Sends this client's requests through the given shield.</summary>
    public static IHttpClientBuilder AddShield(this IHttpClientBuilder builder, Shield<HttpResponseMessage> shield)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        return AddShieldHandler(builder, services =>
            new ShieldDelegatingHandler(
                Decorate(services, shield, builder.Name),
                new ShieldHttpHandlerOptions(),
                CreateDecorator(services, builder.Name)));
    }

    /// <summary>
    /// Sends this client's requests through the named result-aware shield registered with
    /// <see cref="KevlarServiceCollectionExtensions.AddShield{TResult}(IServiceCollection, string, Shield{TResult})"/>.
    /// The registry is queried for every request so reloads and dynamic replacements are observed.
    /// </summary>
    public static IHttpClientBuilder AddShield(this IHttpClientBuilder builder, string shieldName)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (shieldName is null)
        {
            throw new ArgumentNullException(nameof(shieldName));
        }

        builder.Services.AddKevlar();
        return AddShieldHandler(builder, services =>
        {
            var registry = services.GetRequiredService<IKevlarRegistry>();
            return new ShieldDelegatingHandler(
                _ => registry.GetShield<HttpResponseMessage>(shieldName),
                new ShieldHttpHandlerOptions(),
                CreateDecorator(services, builder.Name));
        });
    }

    /// <summary>
    /// Removes previously registered <see cref="ShieldDelegatingHandler"/> instances from this
    /// client's handler pipeline and their standard-shield timeout overrides while preserving
    /// other delegating handlers and client configuration.
    /// </summary>
    public static IHttpClientBuilder RemoveAllShields(this IHttpClientBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        for (var index = builder.Services.Count - 1; index >= 0; index--)
        {
            var descriptor = builder.Services[index];
            if (descriptor.ServiceType == typeof(IConfigureOptions<HttpClientFactoryOptions>)
                && descriptor.ImplementationInstance is ShieldHandlerConfiguration configuration
                && configuration.Name == builder.Name)
            {
                builder.Services.RemoveAt(index);
            }
        }

        builder.Services.Configure<HttpClientFactoryOptions>(
            builder.Name,
            static options =>
            {
                for (var index = options.HttpMessageHandlerBuilderActions.Count - 1; index >= 0; index--)
                {
                    if (options.HttpMessageHandlerBuilderActions[index].Target is ShieldHandlerConfiguration)
                    {
                        options.HttpMessageHandlerBuilderActions.RemoveAt(index);
                    }
                }

                for (var index = options.HttpClientActions.Count - 1; index >= 0; index--)
                {
                    if (options.HttpClientActions[index].Target is StandardTimeoutConfiguration)
                    {
                        options.HttpClientActions.RemoveAt(index);
                    }
                }
            });

        return builder.ConfigureAdditionalHttpMessageHandlers(static (handlers, _) =>
        {
            for (var index = handlers.Count - 1; index >= 0; index--)
            {
                if (handlers[index] is ShieldDelegatingHandler)
                {
                    handlers.RemoveAt(index);
                }
            }
        });
    }

    /// <summary>Sends this client's requests through the given shield with replay and routing options.</summary>
    public static IHttpClientBuilder AddShield(
        this IHttpClientBuilder builder,
        Shield<HttpResponseMessage> shield,
        ShieldHttpHandlerOptions options)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var optionsSnapshot = Snapshot(options);
        var registration = RegisterSharedPipeline(builder);
        return AddShieldHandler(builder, services =>
        {
            var registry = services.GetRequiredService<HttpShieldPipelineRegistry>();
            var pipeline = registry.GetOrAdd(
                registration,
                () => new HttpShieldPipeline(
                    Decorate(registry.Services, shield, builder.Name),
                    optionsSnapshot));
            return new ShieldDelegatingHandler(
                pipeline,
                CreateDecorator(registry.Services, builder.Name));
        });
    }

    /// <summary>
    /// Sends this client's requests through one shared shield built from the application service provider.
    /// </summary>
    public static IHttpClientBuilder AddShield(this IHttpClientBuilder builder, Func<IServiceProvider, Shield<HttpResponseMessage>> shieldFactory)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (shieldFactory is null)
        {
            throw new ArgumentNullException(nameof(shieldFactory));
        }

        var registration = RegisterSharedPipeline(builder);
        return AddShieldHandler(builder, services =>
        {
            var registry = services.GetRequiredService<HttpShieldPipelineRegistry>();
            var pipeline = registry.GetOrAdd(
                registration,
                () => new HttpShieldPipeline(
                    Decorate(
                        registry.Services,
                        shieldFactory(registry.Services),
                        builder.Name),
                    new ShieldHttpHandlerOptions()));
            return new ShieldDelegatingHandler(
                pipeline,
                CreateDecorator(registry.Services, builder.Name));
        });
    }

    /// <summary>Selects one shield for each request using the request and service provider.</summary>
    public static IHttpClientBuilder AddShield(
        this IHttpClientBuilder builder,
        Func<HttpRequestMessage, IServiceProvider, Shield<HttpResponseMessage>> shieldSelector)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (shieldSelector is null)
        {
            throw new ArgumentNullException(nameof(shieldSelector));
        }

        return AddShieldHandler(builder, services =>
            new ShieldDelegatingHandler(request =>
                Decorate(services, shieldSelector(request, services), builder.Name),
                new ShieldHttpHandlerOptions(),
                CreateDecorator(services, builder.Name)));
    }

    /// <summary>Selects a bounded partition shield from each request.</summary>
    public static IHttpClientBuilder AddShield<TKey>(
        this IHttpClientBuilder builder,
        PartitionedShield<TKey, HttpResponseMessage> partitions,
        Func<HttpRequestMessage, TKey> keySelector)
        where TKey : notnull
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (partitions is null)
        {
            throw new ArgumentNullException(nameof(partitions));
        }

        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        return AddShieldHandler(builder, services =>
            new ShieldDelegatingHandler(
                request => GetDecoratedPartitionAsync(
                    services,
                    partitions,
                    keySelector(request),
                    builder.Name),
                new ShieldHttpHandlerOptions(),
                CreateDecorator(services, builder.Name)));
    }

    /// <summary>
    /// Sends this client's requests through shared service-provider-created shield and handler options.
    /// </summary>
    public static IHttpClientBuilder AddShield(
        this IHttpClientBuilder builder,
        Func<IServiceProvider, Shield<HttpResponseMessage>> shieldFactory,
        Func<IServiceProvider, ShieldHttpHandlerOptions> optionsFactory)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (shieldFactory is null)
        {
            throw new ArgumentNullException(nameof(shieldFactory));
        }

        if (optionsFactory is null)
        {
            throw new ArgumentNullException(nameof(optionsFactory));
        }

        var registration = RegisterSharedPipeline(builder);
        return AddShieldHandler(builder, services =>
        {
            var registry = services.GetRequiredService<HttpShieldPipelineRegistry>();
            var pipeline = registry.GetOrAdd(
                registration,
                () => new HttpShieldPipeline(
                    Decorate(
                        registry.Services,
                        shieldFactory(registry.Services),
                        builder.Name),
                    optionsFactory(registry.Services)));
            return new ShieldDelegatingHandler(
                pipeline,
                CreateDecorator(registry.Services, builder.Name));
        });
    }

    /// <summary>
    /// Sends this client's requests through <see cref="HttpShield.Standard()"/> and uses its
    /// attempt timeout instead of <see cref="HttpClient.Timeout"/>.
    /// </summary>
    public static IHttpClientBuilder AddStandardShield(this IHttpClientBuilder builder)
        => AddStandardShield(builder, static _ => { });

    /// <summary>
    /// Configures and adds one shared standard shield for this client registration. The standard
    /// attempt timeout replaces <see cref="HttpClient.Timeout"/>.
    /// </summary>
    public static IHttpClientBuilder AddStandardShield(
        this IHttpClientBuilder builder,
        Action<StandardHttpShieldOptions> configure)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var options = new StandardHttpShieldOptions();
        configure(options);
        var shield = HttpShield.Standard(options);
        var handlerOptions = Snapshot(options.Handler);
        var registration = RegisterSharedPipeline(builder);
        return AddShieldHandler(UseStandardTimeout(builder), services =>
        {
            var registry = services.GetRequiredService<HttpShieldPipelineRegistry>();
            var pipeline = registry.GetOrAdd(
                registration,
                () => new HttpShieldPipeline(
                    Decorate(registry.Services, shield, builder.Name),
                    handlerOptions));
            return new ShieldDelegatingHandler(
                pipeline,
                CreateDecorator(registry.Services, builder.Name));
        });
    }

    /// <summary>
    /// Configures and adds one shared standard shield using the application service provider.
    /// Configuration and shield creation run once for this client registration.
    /// </summary>
    public static IHttpClientBuilder AddStandardShield(
        this IHttpClientBuilder builder,
        Action<IServiceProvider, StandardHttpShieldOptions> configure)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var registration = RegisterSharedPipeline(builder);
        return AddShieldHandler(UseStandardTimeout(builder), services =>
        {
            var registry = services.GetRequiredService<HttpShieldPipelineRegistry>();
            var pipeline = registry.GetOrAdd(
                registration,
                () =>
                {
                    var options = new StandardHttpShieldOptions();
                    configure(registry.Services, options);
                    return new HttpShieldPipeline(
                        Decorate(
                            registry.Services,
                            HttpShield.Standard(options),
                            builder.Name),
                        Snapshot(options.Handler));
                });
            return new ShieldDelegatingHandler(
                pipeline,
                CreateDecorator(registry.Services, builder.Name));
        });
    }

    /// <summary>
    /// Adds a reload-aware standard shield bound from a configuration section.
    /// A valid change replaces the complete pipeline for subsequent requests.
    /// </summary>
    public static IHttpClientBuilder AddStandardShield(
        this IHttpClientBuilder builder,
        IConfiguration configuration,
        Action<Exception>? onReloadFailure = null) =>
        AddStandardShield(
            builder,
            configuration,
            static (_, _) => { },
            onReloadFailure);

    /// <summary>
    /// Adds a reload-aware standard shield bound from configuration, then customized using the
    /// application service provider. The callback runs for the initial snapshot and every reload
    /// after configuration values have been applied.
    /// </summary>
    public static IHttpClientBuilder AddStandardShield(
        this IHttpClientBuilder builder,
        IConfiguration configuration,
        Action<IServiceProvider, StandardHttpShieldOptions> configure,
        Action<Exception>? onReloadFailure = null)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        ValidateStandardConfiguration(configuration);
        var registration = RegisterSharedPipeline(builder);
        return AddShieldHandler(UseStandardTimeout(builder), services =>
        {
            var registry = services.GetRequiredService<HttpShieldPipelineRegistry>();
            var reloadingPipeline = registry.GetOrAdd(
                registration,
                () =>
                {
                    HttpShieldPipeline CreatePipeline()
                    {
                        var options = StandardHttpConfigurationBinder.BindStandard(configuration);
                        configure(registry.Services, options);
                        return new HttpShieldPipeline(
                            Decorate(
                                registry.Services,
                                HttpShield.Standard(options),
                                builder.Name),
                            Snapshot(options.Handler));
                    }

                    return new ReloadingHttpShieldPipeline(
                        CreatePipeline,
                        configuration.GetReloadToken,
                        onReloadFailure);
                });
            return ShieldDelegatingHandler.CreateReloading(
                reloadingPipeline,
                CreateDecorator(registry.Services, builder.Name),
                ownsReloadingPipeline: false);
        });
    }

    private static IHttpClientBuilder UseStandardTimeout(IHttpClientBuilder builder)
    {
        var configuration = new StandardTimeoutConfiguration();
        return builder.ConfigureHttpClient(configuration.Apply);
    }

    private static ShieldHttpHandlerOptions Snapshot(ShieldHttpHandlerOptions source)
    {
        var snapshot = new ShieldHttpHandlerOptions
        {
            ContentReplayPolicy = source.ContentReplayPolicy,
            MaxBufferSize = source.MaxBufferSize,
            AllowUnsafeMethodReplay = source.AllowUnsafeMethodReplay,
            RequestFactory = source.RequestFactory,
            Routing = Snapshot(source.Routing),
        };
        return snapshot;
    }

    private static HttpEndpointRoutingOptions? Snapshot(HttpEndpointRoutingOptions? source)
    {
        if (source is null)
        {
            return null;
        }

        var snapshot = new HttpEndpointRoutingOptions
        {
            SelectionMode = source.SelectionMode,
            Seed = source.Seed,
            ShieldFactory = source.ShieldFactory,
        };
        foreach (var endpoint in source.Endpoints)
        {
            snapshot.Endpoints.Add(endpoint);
        }

        return snapshot;
    }

    /// <summary>
    /// Adds one shared standard pipeline that hedges against the request's authority: total timeout,
    /// hedging, optional per-authority concurrency limit, circuit breaker, then per-attempt timeout.
    /// </summary>
    public static IHttpClientBuilder AddStandardHedgeShield(this IHttpClientBuilder builder) =>
        AddStandardHedgeShield(builder, static _ => { });

    /// <summary>
    /// Adds one shared standard pipeline with optional endpoint routing: total timeout, hedging,
    /// optional per-authority concurrency limit, circuit breaker, then per-attempt timeout.
    /// </summary>
    public static IHttpClientBuilder AddStandardHedgeShield(
        this IHttpClientBuilder builder,
        Action<StandardHedgeShieldOptions> configure)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var options = new StandardHedgeShieldOptions();
        configure(options);
        var shield = CreateHedgeShield(options);
        var handlerOptions = CreateHandlerOptions(options);
        var registration = RegisterSharedPipeline(builder);

        return AddShieldHandler(UseStandardTimeout(builder), services =>
        {
            var registry = services.GetRequiredService<HttpShieldPipelineRegistry>();
            var pipeline = registry.GetOrAdd(
                registration,
                () => new HttpShieldPipeline(
                    Decorate(registry.Services, shield, builder.Name),
                    handlerOptions));
            return new ShieldDelegatingHandler(
                pipeline,
                CreateDecorator(registry.Services, builder.Name));
        });
    }

    /// <summary>
    /// Adds a reload-aware standard hedging shield bound from a configuration section.
    /// A valid change replaces the complete pipeline and all endpoint-local state.
    /// </summary>
    public static IHttpClientBuilder AddStandardHedgeShield(
        this IHttpClientBuilder builder,
        IConfiguration configuration,
        Action<Exception>? onReloadFailure = null) =>
        AddStandardHedgeShield(
            builder,
            configuration,
            static (_, _) => { },
            onReloadFailure);

    /// <summary>
    /// Adds a reload-aware standard hedging shield bound from configuration, then customized
    /// using the application service provider. The callback runs for every snapshot after
    /// configuration values have been applied.
    /// </summary>
    public static IHttpClientBuilder AddStandardHedgeShield(
        this IHttpClientBuilder builder,
        IConfiguration configuration,
        Action<IServiceProvider, StandardHedgeShieldOptions> configure,
        Action<Exception>? onReloadFailure = null)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        ValidateHedgeConfiguration(configuration);
        var registration = RegisterSharedPipeline(builder);
        return AddShieldHandler(UseStandardTimeout(builder), services =>
        {
            var registry = services.GetRequiredService<HttpShieldPipelineRegistry>();
            var reloadingPipeline = registry.GetOrAdd(
                registration,
                () =>
                {
                    HttpShieldPipeline CreatePipeline()
                    {
                        var options = StandardHttpConfigurationBinder.BindHedge(configuration);
                        configure(registry.Services, options);
                        return new HttpShieldPipeline(
                            Decorate(
                                registry.Services,
                                CreateHedgeShield(options),
                                builder.Name),
                            CreateHandlerOptions(options));
                    }

                    return new ReloadingHttpShieldPipeline(
                        CreatePipeline,
                        configuration.GetReloadToken,
                        onReloadFailure);
                });
            return ShieldDelegatingHandler.CreateReloading(
                reloadingPipeline,
                CreateDecorator(registry.Services, builder.Name),
                ownsReloadingPipeline: false);
        });
    }

    private static void ValidateStandardConfiguration(IConfiguration configuration)
    {
        var options = StandardHttpConfigurationBinder.BindStandard(configuration);
        _ = new HttpShieldPipeline(HttpShield.Standard(options), Snapshot(options.Handler));
    }

    private static HttpShieldPipelineRegistration RegisterSharedPipeline(IHttpClientBuilder builder)
    {
        builder.Services.TryAddSingleton(static services => new HttpShieldPipelineRegistry(services));
        return new HttpShieldPipelineRegistration(builder.Name);
    }

    private static IHttpClientBuilder AddShieldHandler(
        IHttpClientBuilder builder,
        Func<IServiceProvider, DelegatingHandler> handlerFactory)
    {
        var configuration = new ShieldHandlerConfiguration(builder.Name, handlerFactory);
        builder.Services.AddSingleton<IConfigureOptions<HttpClientFactoryOptions>>(configuration);
        return builder;
    }

    private static Shield<TResult> Decorate<TResult>(
        IServiceProvider serviceProvider,
        Shield<TResult> shield,
        string? name) =>
        ShieldDecoration.Apply(
            shield,
            name,
            serviceProvider.GetServices<IShieldDecorator>());

    private static Func<Shield<HttpResponseMessage>, Shield<HttpResponseMessage>> CreateDecorator(
        IServiceProvider serviceProvider,
        string? name) =>
        shield => Decorate(serviceProvider, shield, name);

    private static async ValueTask<Shield<TResult>> GetDecoratedPartitionAsync<TKey, TResult>(
        IServiceProvider serviceProvider,
        PartitionedShield<TKey, TResult> partitions,
        TKey key,
        string? name)
        where TKey : notnull =>
        Decorate(
            serviceProvider,
            await partitions.GetShieldAsync(key).ConfigureAwait(false),
            name);

    private static void ValidateHedgeConfiguration(IConfiguration configuration)
    {
        var options = StandardHttpConfigurationBinder.BindHedge(configuration);
        _ = new HttpShieldPipeline(CreateHedgeShield(options), CreateHandlerOptions(options));
    }

    private static Shield<HttpResponseMessage> CreateHedgeShield(StandardHedgeShieldOptions options)
    {
        ValidateHedgeOptions(options);
        var pipelineStart = IsDisabled(options.TotalTimeout)
            ? Shield.For<HttpResponseMessage>()
            : Shield.Timeout(target => Copy(options.TotalTimeout, target))
                .For<HttpResponseMessage>();
        return HttpShield.WhenTransient(pipelineStart)
            .Or<ConcurrencyLimitExceededException>()
            .Or<CircuitOpenException>()
            .Hedge(target => Copy(options.Hedge, target));
    }

    private static ShieldHttpHandlerOptions CreateHandlerOptions(StandardHedgeShieldOptions options)
    {
        var configuredRouting = options.Routing;
        if (configuredRouting is not null
            && !Enum.IsDefined(typeof(HttpEndpointSelectionMode), configuredRouting.SelectionMode))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The endpoint selection mode is invalid.");
        }

        if (!Enum.IsDefined(typeof(HttpContentReplayPolicy), options.Handler.ContentReplayPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ContentReplayPolicy is invalid.");
        }

        if (options.Handler.MaxBufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxBufferSize must be positive.");
        }

        if (configuredRouting?.Endpoints.Any(static endpoint => endpoint is null) is true)
        {
            throw new ArgumentException("Endpoints cannot contain null.", nameof(options));
        }

        var routing = new HttpEndpointRoutingOptions { ShieldFactory = CreateEndpointShieldFactory(options) };
        if (configuredRouting is not null)
        {
            routing.SelectionMode = configuredRouting.SelectionMode;
            routing.Seed = configuredRouting.Seed;
            foreach (var endpoint in configuredRouting.Endpoints)
            {
                routing.Endpoints.Add(endpoint);
            }
        }

        return new ShieldHttpHandlerOptions
        {
            ContentReplayPolicy = options.Handler.ContentReplayPolicy,
            MaxBufferSize = options.Handler.MaxBufferSize,
            AllowUnsafeMethodReplay = options.Handler.AllowUnsafeMethodReplay,
            RequestFactory = options.Handler.RequestFactory,
            Routing = routing,
        };
    }

    private static Func<Uri, Shield<HttpResponseMessage>> CreateEndpointShieldFactory(
        StandardHedgeShieldOptions options)
    {
        var concurrencyLimit = options.ConcurrencyLimit is null
            ? null
            : Clone(options.ConcurrencyLimit);
        var circuitBreaker = Clone(options.CircuitBreaker);
        var attemptTimeout = Clone(options.AttemptTimeout);

        Shield<HttpResponseMessage> CreateEndpointShield(Uri _)
        {
            var shield = concurrencyLimit is null
                ? HttpShield.WhenTransient()
                    .CircuitBreaker(target => Copy(circuitBreaker, target))
                : HttpShield.WhenTransient()
                    .ConcurrencyLimit(target => Copy(concurrencyLimit, target))
                    .CircuitBreaker(target => Copy(circuitBreaker, target));
            return IsDisabled(attemptTimeout)
                ? shield
                : shield.Timeout(target => Copy(attemptTimeout, target));
        }

        _ = CreateEndpointShield(null!);
        return CreateEndpointShield;
    }

    private static void ValidateHedgeOptions(StandardHedgeShieldOptions options)
    {
        if (options.TotalTimeout is null
            || options.Hedge is null
            || options.CircuitBreaker is null
            || options.AttemptTimeout is null
            || options.Handler is null)
        {
            throw new ArgumentException(
                "StandardHedgeShieldOptions required nested options cannot be null.",
                nameof(options));
        }
    }

    private static bool IsDisabled(TimeoutOptions options) =>
        options.Timeout == Timeout.InfiniteTimeSpan;

    private static TimeoutOptions Clone(TimeoutOptions source)
    {
        var target = new TimeoutOptions();
        Copy(source, target);
        return target;
    }

    private static ConcurrencyLimitOptions Clone(ConcurrencyLimitOptions source)
    {
        var target = new ConcurrencyLimitOptions();
        Copy(source, target);
        return target;
    }

    private static CircuitBreakerOptions<HttpResponseMessage> Clone(
        CircuitBreakerOptions<HttpResponseMessage> source)
    {
        var target = new CircuitBreakerOptions<HttpResponseMessage>();
        Copy(source, target);
        return target;
    }

    private static void Copy(TimeoutOptions source, TimeoutOptions target)
    {
        target.Name = source.Name;
        target.Timeout = source.Timeout;
        target.TimeoutGenerator = source.TimeoutGenerator;
        target.OnTimeout = source.OnTimeout;
    }

    private static void Copy(
        HedgeOptions<HttpResponseMessage> source,
        HedgeOptions<HttpResponseMessage> target)
    {
        target.Name = source.Name;
        target.HandlesException = source.HandlesException;
        target.HandlesExceptionContext = source.HandlesExceptionContext;
        target.HandlesResult = source.HandlesResult;
        target.HandlesResultContext = source.HandlesResultContext;
        target.MaxHedgedAttempts = source.MaxHedgedAttempts;
        target.Delay = source.Delay;
        target.DelayGenerator = source.DelayGenerator;
        target.OnHedge = source.OnHedge;
        target.ActionGenerator = source.ActionGenerator;
    }

    private static void Copy(ConcurrencyLimitOptions source, ConcurrencyLimitOptions target)
    {
        target.Name = source.Name;
        target.MaxConcurrency = source.MaxConcurrency;
        target.QueueLimit = source.QueueLimit;
        target.OnRejected = source.OnRejected;
    }

    private static void Copy(
        CircuitBreakerOptions<HttpResponseMessage> source,
        CircuitBreakerOptions<HttpResponseMessage> target)
    {
        target.Name = source.Name;
        target.HandlesException = source.HandlesException;
        target.HandlesExceptionContext = source.HandlesExceptionContext;
        target.HandlesResult = source.HandlesResult;
        target.HandlesResultContext = source.HandlesResultContext;
        target.ConsecutiveFailures = source.ConsecutiveFailures;
        target.FailureRatio = source.FailureRatio;
        target.MinimumThroughput = source.MinimumThroughput;
        target.SamplingWindow = source.SamplingWindow;
        target.BreakDuration = source.BreakDuration;
        target.BreakDurationGenerator = source.BreakDurationGenerator;
        target.Monitor = source.Monitor;
        target.OnStateChanged = source.OnStateChanged;
    }

    private sealed class StandardTimeoutConfiguration
    {
        public void Apply(HttpClient client) => client.Timeout = Timeout.InfiniteTimeSpan;
    }

    private sealed class ShieldHandlerConfiguration : IConfigureNamedOptions<HttpClientFactoryOptions>
    {
        private readonly Func<IServiceProvider, DelegatingHandler> _handlerFactory;

        public ShieldHandlerConfiguration(
            string? name,
            Func<IServiceProvider, DelegatingHandler> handlerFactory)
        {
            Name = name;
            _handlerFactory = handlerFactory;
        }

        public string? Name { get; }

        public void Configure(HttpClientFactoryOptions options)
        {
        }

        public void Configure(string? name, HttpClientFactoryOptions options)
        {
            if (Name is null || name == Name)
            {
                options.HttpMessageHandlerBuilderActions.Add(builder =>
                    builder.AdditionalHandlers.Add(_handlerFactory(builder.Services)));
            }
        }
    }
}

#pragma warning restore RS0026
