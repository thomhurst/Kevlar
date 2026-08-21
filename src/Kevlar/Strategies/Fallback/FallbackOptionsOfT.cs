namespace Kevlar;

/// <summary>
/// Configures result-typed notifications for a fallback strategy on a
/// <see cref="Shield{TResult}"/>.
/// </summary>
/// <remarks>
/// After an outcome is selected for recovery, Kevlar records the fallback metric, invokes
/// <see cref="OnFallback"/>, awaits <see cref="OnFallbackAsync"/>, and then runs the recovery
/// factory. A notification failure skips recovery and becomes the execution outcome.
/// </remarks>
public sealed class FallbackOptions<TResult>
{
    /// <summary>Invoked synchronously before the recovery factory, with the typed handled outcome.</summary>
    public Action<FallbackEvent<TResult>>? OnFallback { get; set; }

    /// <summary>
    /// Invoked and awaited after <see cref="OnFallback"/> and before the recovery factory,
    /// with the typed handled outcome.
    /// </summary>
    public Func<FallbackEvent<TResult>, ValueTask>? OnFallbackAsync { get; set; }
}
