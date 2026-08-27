namespace Kevlar.Extensions.Http;

/// <summary>Thrown when another HTTP attempt cannot be created safely.</summary>
public sealed class HttpRequestReplayException : KevlarException
{
    /// <summary>Creates a replay error with a default message.</summary>
    public HttpRequestReplayException()
        : base("The HTTP request could not be replayed safely.")
    {
    }

    /// <summary>Creates a replay error with actionable details.</summary>
    public HttpRequestReplayException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a replay error caused by request-content serialization.</summary>
    public HttpRequestReplayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
