using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Extensions.Http;

/// <summary>Adds Kevlar resilience to <see cref="IHttpClientBuilder"/> pipelines.</summary>
public static class KevlarHttpClientBuilderExtensions
{
    /// <summary>Sends this client's requests through the given policy.</summary>
    public static IHttpClientBuilder AddKevlar(this IHttpClientBuilder builder, Policy<HttpResponseMessage> policy)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (policy is null) { throw new ArgumentNullException(nameof(policy)); }

        return builder.AddHttpMessageHandler(() => new KevlarDelegatingHandler(policy));
    }

    /// <summary>Sends this client's requests through a policy built from the service provider.</summary>
    public static IHttpClientBuilder AddKevlar(this IHttpClientBuilder builder, Func<IServiceProvider, Policy<HttpResponseMessage>> policyFactory)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (policyFactory is null) { throw new ArgumentNullException(nameof(policyFactory)); }

        return builder.AddHttpMessageHandler(services => new KevlarDelegatingHandler(policyFactory(services)));
    }

    /// <summary>Sends this client's requests through <see cref="HttpKevlar.StandardPolicy"/>.</summary>
    public static IHttpClientBuilder AddStandardKevlar(this IHttpClientBuilder builder)
    {
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }

        var policy = HttpKevlar.StandardPolicy();
        return builder.AddHttpMessageHandler(() => new KevlarDelegatingHandler(policy));
    }
}
