using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Extensions.Http;

/// <summary>Adds Kevlar resilience to <see cref="IHttpClientBuilder"/> pipelines.</summary>
public static class ShieldHttpClientBuilderExtensions
{
    /// <summary>Sends this client's requests through the given shield.</summary>
    public static IHttpClientBuilder AddShield(this IHttpClientBuilder builder, Shield<HttpResponseMessage> shield)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }

        return builder.AddHttpMessageHandler(() => new ShieldDelegatingHandler(shield));
    }

    /// <summary>Sends this client's requests through the given shield with replay and routing options.</summary>
    public static IHttpClientBuilder AddShield(
        this IHttpClientBuilder builder,
        Shield<HttpResponseMessage> shield,
        ShieldHttpHandlerOptions options)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }
        if (options is null) { throw new ArgumentNullException(nameof(options)); }

        return builder.AddHttpMessageHandler(() => new ShieldDelegatingHandler(shield, options));
    }

    /// <summary>Sends this client's requests through a shield built from the service provider.</summary>
    public static IHttpClientBuilder AddShield(this IHttpClientBuilder builder, Func<IServiceProvider, Shield<HttpResponseMessage>> shieldFactory)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (shieldFactory is null) { throw new ArgumentNullException(nameof(shieldFactory)); }

        return builder.AddHttpMessageHandler(services => new ShieldDelegatingHandler(shieldFactory(services)));
    }

    /// <summary>Sends this client's requests through service-provider-created shield and handler options.</summary>
    public static IHttpClientBuilder AddShield(
        this IHttpClientBuilder builder,
        Func<IServiceProvider, Shield<HttpResponseMessage>> shieldFactory,
        Func<IServiceProvider, ShieldHttpHandlerOptions> optionsFactory)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (shieldFactory is null) { throw new ArgumentNullException(nameof(shieldFactory)); }
        if (optionsFactory is null) { throw new ArgumentNullException(nameof(optionsFactory)); }

        return builder.AddHttpMessageHandler(services =>
            new ShieldDelegatingHandler(shieldFactory(services), optionsFactory(services)));
    }

    /// <summary>Sends this client's requests through <see cref="HttpShield.Standard"/>.</summary>
    public static IHttpClientBuilder AddStandardShield(this IHttpClientBuilder builder)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }

        var shield = HttpShield.Standard();
        return builder.AddHttpMessageHandler(() => new ShieldDelegatingHandler(shield));
    }
}
