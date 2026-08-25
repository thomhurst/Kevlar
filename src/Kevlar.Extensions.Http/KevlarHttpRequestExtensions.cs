namespace Kevlar.Extensions.Http;

/// <summary>Configures Kevlar behavior on individual HTTP requests.</summary>
public static class KevlarHttpRequestExtensions
{
    /// <summary>Adds an initializer for properties visible to strategies and callbacks.</summary>
    public static HttpRequestMessage WithKevlarProperties(
        this HttpRequestMessage request,
        Action<KevlarProperties> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var options = KevlarHttp.GetRequestOptions(request);
        options.ConfigureProperties += configure;
        return request;
    }

    /// <summary>Uses <paramref name="shield"/> instead of the client's default for this request.</summary>
    public static HttpRequestMessage WithShield(
        this HttpRequestMessage request,
        Shield<HttpResponseMessage> shield)
    {
        if (shield is null)
        {
            throw new ArgumentNullException(nameof(shield));
        }

        KevlarHttp.GetRequestOptions(request).Shield = shield;
        return request;
    }

    /// <summary>Sets the logical name consumed by a per-request shield selector.</summary>
    public static HttpRequestMessage WithShieldName(this HttpRequestMessage request, string shieldName)
    {
        if (shieldName is null)
        {
            throw new ArgumentNullException(nameof(shieldName));
        }

        KevlarHttp.GetRequestOptions(request).ShieldName = shieldName;
        return request;
    }

    /// <summary>Suppresses retries, hedges, and other additional attempts for this request.</summary>
    public static HttpRequestMessage DisableReplay(this HttpRequestMessage request)
    {
        KevlarHttp.GetRequestOptions(request).AllowReplay = false;
        return request;
    }

    /// <summary>Links an additional cancellation token to this request's handler execution.</summary>
    public static HttpRequestMessage WithKevlarCancellationToken(
        this HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        KevlarHttp.GetRequestOptions(request).CancellationToken = cancellationToken;
        return request;
    }
}
