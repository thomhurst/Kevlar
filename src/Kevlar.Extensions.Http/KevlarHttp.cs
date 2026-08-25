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
