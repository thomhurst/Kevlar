namespace Kevlar.Chaos;

/// <summary>Identifies the kind of chaos injected into an execution.</summary>
public enum ChaosInjectionKind
{
    /// <summary>An artificial delay.</summary>
    Latency,

    /// <summary>An exception outcome.</summary>
    Fault,

    /// <summary>A caller-supplied result outcome.</summary>
    Outcome,

    /// <summary>Caller-supplied asynchronous behavior.</summary>
    Behavior,
}
