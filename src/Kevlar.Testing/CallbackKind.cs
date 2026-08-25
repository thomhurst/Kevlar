namespace Kevlar.Testing;

/// <summary>Identifies a Kevlar strategy callback captured by <see cref="TelemetryRecorder"/>.</summary>
public enum CallbackKind
{
    /// <summary>A retry callback.</summary>
    Retry,

    /// <summary>A timeout callback.</summary>
    Timeout,

    /// <summary>A hedge callback.</summary>
    Hedge,

    /// <summary>A fallback callback.</summary>
    Fallback,

    /// <summary>A circuit-breaker transition callback.</summary>
    CircuitTransition,

    /// <summary>A circuit-breaker break-duration callback.</summary>
    CircuitBreakDuration,
}
