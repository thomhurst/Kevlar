namespace Kevlar.Chaos;

/// <summary>Describes one chaos injection.</summary>
/// <remarks>
/// <see cref="Context"/> is pooled and valid only for the duration of the callback. Copy any
/// values that must be retained.
/// </remarks>
public readonly struct ChaosEvent
{
    internal ChaosEvent(
        ChaosInjectionKind kind,
        KevlarContext context,
        string? operation,
        string? environment,
        double injectionRate,
        double sample)
    {
        Kind = kind;
        Context = context;
        Operation = operation;
        Environment = environment;
        InjectionRate = injectionRate;
        Sample = sample;
    }

    /// <summary>Gets the injected behavior kind.</summary>
    public ChaosInjectionKind Kind { get; }

    /// <summary>Gets the current Kevlar execution context.</summary>
    public KevlarContext Context { get; }

    /// <summary>Gets the operation from the active <see cref="ChaosScope"/>, if any.</summary>
    public string? Operation { get; }

    /// <summary>Gets the environment from the active <see cref="ChaosScope"/>, if any.</summary>
    public string? Environment { get; }

    /// <summary>Gets the effective injection rate used for this decision.</summary>
    public double InjectionRate { get; }

    /// <summary>Gets the random sample used for this decision.</summary>
    public double Sample { get; }
}
