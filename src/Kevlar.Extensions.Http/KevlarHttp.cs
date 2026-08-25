namespace Kevlar.Extensions.Http;

/// <summary>Accesses Kevlar-specific options attached to an HTTP request.</summary>
public static class KevlarHttp
{
    private const string RequestOptionsName = "Kevlar.RequestOptions";
#if !NETSTANDARD2_0
    /// <summary>The typed key used to attach Kevlar options to modern HTTP requests.</summary>
    public static HttpRequestOptionsKey<KevlarRequestOptions> RequestOptions { get; } =
        new(RequestOptionsName);
#endif

    /// <summary>Gets or creates the Kevlar options for <paramref name="request"/>.</summary>
    public static KevlarRequestOptions GetRequestOptions(HttpRequestMessage request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (TryGetRequestOptions(request, out var options))
        {
            return options!;
        }

        options = new KevlarRequestOptions();
#if NETSTANDARD2_0
#pragma warning disable CS0618 // HttpRequestMessage.Properties is the netstandard2.0 compatibility path.
        request.Properties[RequestOptionsName] = options;
#pragma warning restore CS0618
#else
        request.Options.Set(RequestOptions, options);
#endif
        return options;
    }

    internal static bool TryGetRequestOptions(
        HttpRequestMessage request,
        out KevlarRequestOptions? options)
    {
#if NETSTANDARD2_0
#pragma warning disable CS0618 // HttpRequestMessage.Properties is the netstandard2.0 compatibility path.
        if (request.Properties.TryGetValue(RequestOptionsName, out var value)
            && value is KevlarRequestOptions found)
#pragma warning restore CS0618
#else
        if (request.Options.TryGetValue(RequestOptions, out var found)
            && found is not null)
#endif
        {
            options = found;
            return true;
        }

        options = null;
        return false;
    }
}

/// <summary>Configures one HTTP request's Kevlar execution.</summary>
public sealed class KevlarRequestOptions
{
    /// <summary>Initializes the pooled execution properties before the selected shield runs.</summary>
    public Action<KevlarProperties>? ConfigureProperties { get; set; }

    /// <summary>Overrides the client's shield for this request.</summary>
    public Shield<HttpResponseMessage>? Shield { get; set; }

    /// <summary>Optional logical shield name available to per-request selectors.</summary>
    public string? ShieldName { get; set; }

    /// <summary>
    /// Overrides replay permission. <see langword="false"/> suppresses retries and hedges;
    /// <see langword="true"/> permits unsafe HTTP methods subject to content replay safety.
    /// </summary>
    public bool? AllowReplay { get; set; }

    /// <summary>An additional cancellation token linked with the token supplied to the handler.</summary>
    public CancellationToken CancellationToken { get; set; }
}

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
