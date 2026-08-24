namespace Kevlar;

/// <summary>Base class for exceptions raised by Kevlar strategies themselves.</summary>
public abstract class KevlarException : Exception
{
    /// <summary>Initializes the exception.</summary>
    protected KevlarException()
    {
    }

    /// <summary>Initializes the exception with a message.</summary>
    protected KevlarException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and inner exception.</summary>
    protected KevlarException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
