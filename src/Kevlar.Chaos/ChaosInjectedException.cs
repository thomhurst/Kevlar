namespace Kevlar.Chaos;

/// <summary>The default exception produced by fault injection.</summary>
public sealed class ChaosInjectedException : Exception
{
    /// <summary>Initializes a new chaos-injected exception.</summary>
    public ChaosInjectedException()
        : base("A fault was injected by Kevlar.Chaos.")
    {
    }

    /// <summary>Initializes a new chaos-injected exception with a custom message.</summary>
    /// <param name="message">The exception message.</param>
    public ChaosInjectedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new chaos-injected exception with a custom message and inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public ChaosInjectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
