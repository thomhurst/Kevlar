namespace Kevlar.Extensions.Http;

/// <summary>An alternate HTTP authority and its relative routing weight.</summary>
public sealed class HttpEndpoint
{
    /// <summary>Creates an endpoint. The URI must be absolute and <paramref name="weight"/> positive.</summary>
    public HttpEndpoint(Uri uri, int weight = 1)
    {
        if (uri is null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("The endpoint URI must be absolute.", nameof(uri));
        }

        if (weight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weight), "Weight must be positive.");
        }

        Uri = uri;
        Weight = weight;
    }

    /// <summary>The authority used for routed attempts. The original path and query are preserved.</summary>
    public Uri Uri { get; }

    /// <summary>The endpoint's relative weight.</summary>
    public int Weight { get; }
}
