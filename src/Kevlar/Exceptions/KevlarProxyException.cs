namespace Kevlar;

/// <summary>
/// Base class for adapter-owned exceptions that preserve transport bookkeeping while exposing the
/// original failure to Kevlar's handling clauses and public outcomes.
/// </summary>
public abstract class KevlarProxyException : Exception
{
    /// <summary>Initializes the proxy with the exception public outcome APIs should expose.</summary>
    protected KevlarProxyException(Exception originalException)
        : base(
            originalException?.Message ?? throw new ArgumentNullException(nameof(originalException)),
            originalException)
    {
        OriginalException = originalException;
    }

    /// <summary>The original exception exposed by handling clauses and public outcome APIs.</summary>
    public Exception OriginalException { get; }
}
