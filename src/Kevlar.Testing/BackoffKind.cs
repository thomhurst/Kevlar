namespace Kevlar.Testing;

/// <summary>Stable category for an inert backoff descriptor.</summary>
public enum BackoffKind
{
    /// <summary>No delay between attempts.</summary>
    None,

    /// <summary>A constant delay.</summary>
    Constant,

    /// <summary>A linearly increasing delay.</summary>
    Linear,

    /// <summary>An exponentially increasing delay.</summary>
    Exponential,

    /// <summary>A caller-defined delay callback whose executable implementation is not exposed.</summary>
    Custom,
}
