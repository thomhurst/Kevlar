namespace Kevlar;

/// <summary>Receives synchronous strategy telemetry without changing execution outcomes.</summary>
public interface IKevlarTelemetryListener
{
    /// <summary>Handles one telemetry event.</summary>
    /// <remarks>Exceptions thrown by a listener are ignored by Kevlar.</remarks>
    void OnEvent(in KevlarTelemetryEvent telemetryEvent);
}

internal interface IKevlarResultTelemetryListener
{
    bool ShouldCaptureResult(in KevlarTelemetryEvent telemetryEvent);
}
