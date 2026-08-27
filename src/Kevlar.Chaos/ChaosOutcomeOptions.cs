namespace Kevlar.Chaos;

/// <summary>Configures typed result injection.</summary>
/// <typeparam name="TResult">The result type.</typeparam>
public sealed class ChaosOutcomeOptions<TResult> : ChaosOptions
{
    /// <summary>Gets or sets the result to inject.</summary>
    public TResult? Result { get; set; }

    /// <summary>Gets or sets an awaited callback that creates the result for each injected execution.</summary>
    public Func<KevlarContext, ValueTask<TResult>>? ResultGenerator { get; set; }
}
