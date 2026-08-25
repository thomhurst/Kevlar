namespace Kevlar;

/// <summary>Controls randomization applied to built-in retry backoff delays.</summary>
public enum Jitter
{
    /// <summary>Uses the exact delay produced by the backoff curve.</summary>
    None,

    /// <summary>Scales each delay by a uniformly random factor in [0.5, 1.5).</summary>
    Equal,

    /// <summary>Selects a uniformly random delay in [0, the backoff curve's delay).</summary>
    Full,

    /// <summary>
    /// Selects each delay uniformly between the initial delay and three times the preceding
    /// delay, capped by the configured maximum.
    /// </summary>
    Decorrelated,
}
