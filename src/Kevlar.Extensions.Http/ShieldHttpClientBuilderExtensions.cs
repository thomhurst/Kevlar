using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Extensions.Http;

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
}
