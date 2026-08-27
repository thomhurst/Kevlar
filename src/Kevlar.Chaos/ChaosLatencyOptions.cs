namespace Kevlar.Chaos;

/// <summary>Configures artificial latency injection.</summary>
public sealed class ChaosLatencyOptions : ChaosOptions
{
    /// <summary>Gets or sets the artificial delay. The default is zero.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>Gets or sets an awaited callback that computes the delay for each injected execution.</summary>
    public Func<KevlarContext, ValueTask<TimeSpan>>? DelayGenerator { get; set; }
}
