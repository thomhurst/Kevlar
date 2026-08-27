namespace Kevlar.Chaos;

/// <summary>Common controls for explicitly enabling and bounding chaos injection.</summary>
public abstract class ChaosOptions
{
    /// <summary>An optional low-cardinality name used by strategy telemetry.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets whether injection is permitted. The default is <see langword="false"/>.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the probability of injection, from zero through one. The default is one.</summary>
    public double InjectionRate { get; set; } = 1;

    /// <summary>Gets or sets an awaited callback that computes the injection rate for each execution.</summary>
    /// <remarks>
    /// When set, this callback takes precedence over <see cref="InjectionRate"/>. Return a completed
    /// <see cref="ValueTask{TResult}"/> to retain synchronous-execution compatibility.
    /// </remarks>
    public Func<KevlarContext, ValueTask<double>>? InjectionRateGenerator { get; set; }

    /// <summary>Gets or sets an awaited dynamic kill switch evaluated after <see cref="Enabled"/>.</summary>
    public Func<KevlarContext, ValueTask<bool>>? EnabledGenerator { get; set; }

    /// <summary>Gets or sets an additional execution predicate that bounds the blast radius.</summary>
    public Func<KevlarContext, bool>? Predicate { get; set; }

    /// <summary>Gets or sets the required <see cref="ChaosScope.Operation"/> value.</summary>
    public string? Operation { get; set; }

    /// <summary>Gets or sets the required <see cref="ChaosScope.Environment"/> value.</summary>
    public string? Environment { get; set; }

    /// <summary>Gets or sets an optional deterministic random seed.</summary>
    public int? Seed { get; set; }

    /// <summary>
    /// Gets or sets a callback invoked and awaited immediately before injection. Return
    /// <see langword="default"/> from a synchronous callback.
    /// </summary>
    public Func<ChaosEvent, ValueTask>? OnInjected { get; set; }
}
