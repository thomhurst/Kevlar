namespace Kevlar;

/// <summary>Classifies the operational significance of a Kevlar telemetry event.</summary>
public enum KevlarTelemetrySeverity
{
    /// <summary>Routine diagnostic information.</summary>
    Information,

    /// <summary>A handled failure, delay, or degraded execution.</summary>
    Warning,

    /// <summary>An execution failure that was not recovered.</summary>
    Error,
}
