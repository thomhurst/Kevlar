using Kevlar.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Extensions.Grpc;

/// <summary>Adds Kevlar call resilience to gRPC clients registered through DI.</summary>
public static class ShieldGrpcClientBuilderExtensions
{
    /// <summary>Sends this client's asynchronous unary calls through the given shield.</summary>
    public static IHttpClientBuilder AddShieldUnaryInterceptor(
        this IHttpClientBuilder builder,
        Shield shield)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }

        return builder.AddInterceptor(() => new ShieldUnaryClientInterceptor(shield));
    }

    /// <summary>Sends this client's asynchronous unary calls through a shield resolved from DI.</summary>
    public static IHttpClientBuilder AddShieldUnaryInterceptor(
        this IHttpClientBuilder builder,
        Func<IServiceProvider, Shield> shieldFactory)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (shieldFactory is null) { throw new ArgumentNullException(nameof(shieldFactory)); }

        return builder.AddInterceptor(services =>
            new ShieldUnaryClientInterceptor(shieldFactory(services)));
    }

    /// <summary>
    /// Sends this client's asynchronous unary calls through the named shield registered with
    /// <see cref="KevlarServiceCollectionExtensions.AddShield(IServiceCollection, string, Shield)"/>.
    /// </summary>
    public static IHttpClientBuilder AddShieldUnaryInterceptor(
        this IHttpClientBuilder builder,
        string shieldName)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (shieldName is null) { throw new ArgumentNullException(nameof(shieldName)); }

        return builder.AddInterceptor(services =>
            new ShieldUnaryClientInterceptor(
                services.GetRequiredService<IKevlarRegistry>().GetShield(shieldName)));
    }

    /// <summary>Sends this client's asynchronous streaming operations through the given shield.</summary>
    public static IHttpClientBuilder AddShieldStreamingInterceptor(
        this IHttpClientBuilder builder,
        Shield shield)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }

        return builder.AddInterceptor(() => new ShieldStreamingClientInterceptor(shield));
    }

    /// <summary>Sends this client's asynchronous streaming operations through a shield resolved from DI.</summary>
    public static IHttpClientBuilder AddShieldStreamingInterceptor(
        this IHttpClientBuilder builder,
        Func<IServiceProvider, Shield> shieldFactory)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (shieldFactory is null) { throw new ArgumentNullException(nameof(shieldFactory)); }

        return builder.AddInterceptor(services =>
            new ShieldStreamingClientInterceptor(shieldFactory(services)));
    }

    /// <summary>
    /// Sends this client's asynchronous streaming operations through the named shield registered
    /// with <see cref="KevlarServiceCollectionExtensions.AddShield(IServiceCollection, string, Shield)"/>.
    /// </summary>
    public static IHttpClientBuilder AddShieldStreamingInterceptor(
        this IHttpClientBuilder builder,
        string shieldName)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (shieldName is null) { throw new ArgumentNullException(nameof(shieldName)); }

        return builder.AddInterceptor(services =>
            new ShieldStreamingClientInterceptor(
                services.GetRequiredService<IKevlarRegistry>().GetShield(shieldName)));
    }
}
