namespace Kevlar;

/// <summary>Suggested severity for a structured Kevlar event.</summary>
public enum KevlarEventSeverity
{
    /// <summary>Detailed diagnostic information.</summary>
    Debug,

    /// <summary>Normal lifecycle information.</summary>
    Information,

    /// <summary>A handled or potentially disruptive condition.</summary>
    Warning,

    /// <summary>An execution failure.</summary>
    Error,
}
