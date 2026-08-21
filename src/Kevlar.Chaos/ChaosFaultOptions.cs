namespace Kevlar.Chaos;

/// <summary>Configures exception injection.</summary>
public sealed class ChaosFaultOptions : ChaosOptions
{
    /// <summary>Gets or sets the exception instance to inject.</summary>
    /// <remarks>A <see cref="ChaosInjectedException"/> is used when this value is null.</remarks>
    public Exception? Exception { get; set; }

    /// <summary>Gets or sets a callback that creates the exception for each injected execution.</summary>
    public Func<KevlarContext, Exception>? ExceptionGenerator { get; set; }
}
