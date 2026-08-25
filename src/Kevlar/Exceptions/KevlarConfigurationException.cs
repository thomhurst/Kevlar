namespace Kevlar;

/// <summary>Represents invalid strategy configuration supplied through an options callback.</summary>
public sealed class KevlarConfigurationException : KevlarException
{
    /// <summary>Initializes the exception with the default message.</summary>
    public KevlarConfigurationException()
        : base("The Kevlar strategy configuration is invalid.")
    {
    }

    /// <summary>Initializes the exception with a message.</summary>
    public KevlarConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and inner exception.</summary>
    public KevlarConfigurationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
