namespace Kevlar.Testing;

/// <summary>Thrown when a structured shield assertion fails.</summary>
public sealed class ShieldAssertionException : Exception
{
    /// <summary>Creates an assertion failure with an actionable message.</summary>
    public ShieldAssertionException(string message)
        : base(message)
    {
    }
}
