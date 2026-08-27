namespace Kevlar.Testing;

/// <summary>Identifies a Kevlar strategy callback captured by <see cref="TelemetryRecorder"/>.</summary>
public enum CallbackKind
{
    /// <summary>No callback kind.</summary>
    None = 0,

    /// <summary>A retry callback.</summary>
    Retry = 1,

    /// <summary>A timeout callback.</summary>
    Timeout = 2,

    /// <summary>A hedge callback.</summary>
    Hedge = 3,

    /// <summary>A fallback callback.</summary>
    Fallback = 4,

    /// <summary>A circuit-breaker transition callback.</summary>
    CircuitStateChanged = 5,

    /// <summary>A circuit-breaker break-duration callback.</summary>
    CircuitBreakDuration = 6,

    /// <summary>A failed strategy notification reported by Kevlar diagnostics.</summary>
    CallbackError = 7,
}
