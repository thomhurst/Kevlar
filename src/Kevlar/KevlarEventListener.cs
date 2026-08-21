namespace Kevlar;

/// <summary>Receives the unified stream of structured Kevlar events.</summary>
/// <remarks>
/// Callbacks run synchronously in subscription order and may run concurrently for concurrent
/// executions. Implementations must be thread-safe. Reentrancy is supported. Listener exceptions
/// are suppressed so telemetry cannot alter execution behavior.
/// </remarks>
public abstract class KevlarEventListener
{
    /// <summary>Returns whether this listener wants events of <paramref name="kind"/>.</summary>
    public virtual bool IsEnabled(KevlarEventKind kind) => true;

    /// <summary>Receives an event while preserving its result type.</summary>
    public abstract void OnEvent<T>(in KevlarEvent<T> telemetryEvent);
}
