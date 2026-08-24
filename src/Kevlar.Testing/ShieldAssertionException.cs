namespace Kevlar.Testing;

/// <summary>Thrown when a structured shield assertion fails.</summary>
public sealed class ShieldAssertionException : Exception
{
    /// <summary>Creates an assertion failure with a default message.</summary>
    public ShieldAssertionException()
        : base("A shield assertion failed.")
    {
    }

    /// <summary>Creates an assertion failure with an actionable message.</summary>
    public ShieldAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an assertion failure with an actionable message and cause.</summary>
    public ShieldAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
