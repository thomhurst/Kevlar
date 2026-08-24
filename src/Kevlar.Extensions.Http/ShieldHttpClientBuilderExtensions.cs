using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Extensions.Http;

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

        return builder.AddHttpMessageHandler(() => new ShieldDelegatingHandler(shield));
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

        return builder.AddHttpMessageHandler(() => new ShieldDelegatingHandler(shield, options));
    }

    /// <summary>Sends this client's requests through a shield built from the service provider.</summary>
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

        return builder.AddHttpMessageHandler(services => new ShieldDelegatingHandler(shieldFactory(services)));
    }

    /// <summary>Sends this client's requests through service-provider-created shield and handler options.</summary>
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

        return builder.AddHttpMessageHandler(services =>
            new ShieldDelegatingHandler(shieldFactory(services), optionsFactory(services)));
    }

    /// <summary>Sends this client's requests through <see cref="HttpShield.Standard()"/>.</summary>
    public static IHttpClientBuilder AddStandardShield(this IHttpClientBuilder builder)
        => AddStandardShield(builder, static _ => { });

    /// <summary>Configures and adds one shared standard shield for this client registration.</summary>
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
        return builder.AddHttpMessageHandler(() => new ShieldDelegatingHandler(shield, handlerOptions));
    }

    /// <summary>
    /// Configures and adds a standard shield using the handler pipeline's service provider.
    /// Configuration and shield creation run once per handler lifetime.
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

        return builder.AddHttpMessageHandler(services =>
        {
            var options = new StandardHttpShieldOptions();
            configure(services, options);
            return new ShieldDelegatingHandler(HttpShield.Standard(options), Snapshot(options.Handler));
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
    /// handler pipeline's service provider. The callback runs for the initial snapshot and every
    /// reload after configuration values have been applied.
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
        return builder.AddHttpMessageHandler(services =>
        {
            HttpShieldPipeline CreatePipeline()
            {
                var options = StandardHttpConfigurationBinder.BindStandard(configuration);
                configure(services, options);
                return new HttpShieldPipeline(
                    HttpShield.Standard(options),
                    Snapshot(options.Handler));
            }

            return ShieldDelegatingHandler.CreateReloading(new ReloadingHttpShieldPipeline(
                CreatePipeline,
                configuration.GetReloadToken,
                onReloadFailure));
        });
    }

    private static ShieldHttpHandlerOptions Snapshot(ShieldHttpHandlerOptions source)
    {
        var snapshot = new ShieldHttpHandlerOptions
        {
            ContentReplayPolicy = source.ContentReplayPolicy,
            MaximumBufferSize = source.MaximumBufferSize,
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
    /// Adds a standard endpoint-aware pipeline: total timeout, hedging, per-endpoint concurrency
    /// limit and circuit breaker, then per-attempt timeout.
    /// </summary>
    public static IHttpClientBuilder AddStandardHedgingShield(
        this IHttpClientBuilder builder,
        Action<StandardHedgingShieldOptions> configure)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var options = new StandardHedgingShieldOptions();
        configure(options);
        var shield = CreateHedgingShield(options);
        var handlerOptions = CreateHandlerOptions(options);

        return builder.AddHttpMessageHandler(() =>
            new ShieldDelegatingHandler(shield, handlerOptions));
    }

    /// <summary>
    /// Adds a reload-aware standard hedging shield bound from a configuration section.
    /// A valid change replaces the complete pipeline and all endpoint-local state.
    /// </summary>
    public static IHttpClientBuilder AddStandardHedgingShield(
        this IHttpClientBuilder builder,
        IConfiguration configuration,
        Action<Exception>? onReloadFailure = null) =>
        AddStandardHedgingShield(
            builder,
            configuration,
            static (_, _) => { },
            onReloadFailure);

    /// <summary>
    /// Adds a reload-aware standard hedging shield bound from configuration, then customized
    /// using the handler pipeline's service provider. The callback runs for every snapshot after
    /// configuration values have been applied.
    /// </summary>
    public static IHttpClientBuilder AddStandardHedgingShield(
        this IHttpClientBuilder builder,
        IConfiguration configuration,
        Action<IServiceProvider, StandardHedgingShieldOptions> configure,
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

        ValidateHedgingConfiguration(configuration);
        return builder.AddHttpMessageHandler(services =>
        {
            HttpShieldPipeline CreatePipeline()
            {
                var options = StandardHttpConfigurationBinder.BindHedging(configuration);
                configure(services, options);
                return new HttpShieldPipeline(
                    CreateHedgingShield(options),
                    CreateHandlerOptions(options));
            }

            return ShieldDelegatingHandler.CreateReloading(new ReloadingHttpShieldPipeline(
                CreatePipeline,
                configuration.GetReloadToken,
                onReloadFailure));
        });
    }

    private static void ValidateStandardConfiguration(IConfiguration configuration)
    {
        var options = StandardHttpConfigurationBinder.BindStandard(configuration);
        _ = new HttpShieldPipeline(HttpShield.Standard(options), Snapshot(options.Handler));
    }

    private static void ValidateHedgingConfiguration(IConfiguration configuration)
    {
        var options = StandardHttpConfigurationBinder.BindHedging(configuration);
        _ = new HttpShieldPipeline(CreateHedgingShield(options), CreateHandlerOptions(options));
    }

    private static Shield<HttpResponseMessage> CreateHedgingShield(StandardHedgingShieldOptions options) =>
        Shield.Timeout(options.TotalTimeout)
            .For<HttpResponseMessage>()
            .When<HttpRequestException>()
            .Or<TimeoutExceededException>()
            .Or<ConcurrencyLimitExceededException>()
            .Or<CircuitOpenException>()
            .OrResult(HttpShield.IsTransient)
            .Hedge(options.MaxAttempts, options.HedgeDelay);

    private static ShieldHttpHandlerOptions CreateHandlerOptions(StandardHedgingShieldOptions options)
    {
        if (!Enum.IsDefined(typeof(HttpEndpointSelectionMode), options.SelectionMode))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The endpoint selection mode is invalid.");
        }

        if (!Enum.IsDefined(typeof(HttpContentReplayPolicy), options.ContentReplayPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ContentReplayPolicy is invalid.");
        }

        if (options.MaximumBufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumBufferSize must be positive.");
        }

        if (options.Endpoints.Count == 0)
        {
            throw new ArgumentException("Standard hedging requires at least one endpoint.", nameof(options));
        }

        if (options.Endpoints.Any(static endpoint => endpoint is null))
        {
            throw new ArgumentException("Endpoints cannot contain null.", nameof(options));
        }

        var routing = new HttpEndpointRoutingOptions
        {
            SelectionMode = options.SelectionMode,
            Seed = options.Seed,
            ShieldFactory = CreateEndpointShieldFactory(options),
        };
        foreach (var endpoint in options.Endpoints)
        {
            routing.Endpoints.Add(endpoint);
        }

        return new ShieldHttpHandlerOptions
        {
            ContentReplayPolicy = options.ContentReplayPolicy,
            MaximumBufferSize = options.MaximumBufferSize,
            AllowUnsafeMethodReplay = options.AllowUnsafeMethodReplay,
            RequestFactory = options.RequestFactory,
            Routing = routing,
        };
    }

    private static Func<Uri, Shield<HttpResponseMessage>> CreateEndpointShieldFactory(
        StandardHedgingShieldOptions options)
    {
        var maxConcurrency = options.MaxConcurrency;
        var maxQueue = options.MaxQueue;
        var consecutiveFailures = options.ConsecutiveFailures;
        var failureRatio = options.FailureRatio;
        var minimumThroughput = options.MinimumThroughput;
        var samplingWindow = options.SamplingWindow;
        var breakDuration = options.BreakDuration;
        var attemptTimeout = options.AttemptTimeout;

        Shield<HttpResponseMessage> CreateEndpointShield(Uri _) =>
            HttpShield.WhenTransient()
                .ConcurrencyLimit(maxConcurrency, maxQueue)
                .CircuitBreaker(circuitBreaker =>
                {
                    circuitBreaker.ConsecutiveFailures = consecutiveFailures;
                    circuitBreaker.FailureRatio = failureRatio;
                    circuitBreaker.MinimumThroughput = minimumThroughput;
                    circuitBreaker.SamplingWindow = samplingWindow;
                    circuitBreaker.BreakDuration = breakDuration;
                })
                .Timeout(attemptTimeout);

        _ = CreateEndpointShield(null!);
        return CreateEndpointShield;
    }
}

#pragma warning restore RS0026
