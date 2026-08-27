namespace Kevlar.Extensions.Http;

/// <summary>Well-known execution-property keys published by the Kevlar HTTP integration.</summary>
public static class KevlarHttpKeys
{
    /// <summary>The HTTP request method.</summary>
    public static KevlarKey<string> RequestMethod { get; } = new("kevlar.http.request.method");

    /// <summary>The HTTP request URI without its query string or fragment.</summary>
    public static KevlarKey<string> RequestUri { get; } = new("kevlar.http.request.uri");
}
