namespace Kevlar;

/// <summary>The built-in and caller-defined backoff categories.</summary>
public enum BackoffKind
{
    /// <summary>No delay between attempts.</summary>
    None,

    /// <summary>The same delay before every attempt.</summary>
    Constant,

    /// <summary>A linearly increasing delay.</summary>
    Linear,

    /// <summary>An exponentially increasing delay.</summary>
    Exponential,

    /// <summary>A caller-defined delay callback.</summary>
    Custom,
}
