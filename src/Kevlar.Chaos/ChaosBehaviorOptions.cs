namespace Kevlar.Chaos;

/// <summary>Configures caller-supplied behavior injection.</summary>
public sealed class ChaosBehaviorOptions : ChaosOptions
{
    /// <summary>
    /// Gets or sets asynchronous behavior invoked before the shield continues. The default does nothing.
    /// </summary>
    public Func<KevlarContext, ValueTask>? Behavior { get; set; }
}
