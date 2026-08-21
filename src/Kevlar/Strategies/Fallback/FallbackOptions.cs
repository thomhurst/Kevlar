namespace Kevlar;

/// <summary>Configures notifications for an untyped fallback strategy.</summary>
/// <remarks>
/// After a failure is selected for recovery, Kevlar records the fallback metric, invokes
/// <see cref="OnFallback"/>, awaits <see cref="OnFallbackAsync"/>, and then runs the recovery
/// action. A notification failure skips recovery and becomes the execution outcome.
/// </remarks>
public sealed class FallbackOptions
{
    /// <summary>Invoked synchronously before the recovery action.</summary>
    public Action<FallbackEvent>? OnFallback { get; set; }

    /// <summary>Invoked and awaited after <see cref="OnFallback"/> and before the recovery action.</summary>
    public Func<FallbackEvent, ValueTask>? OnFallbackAsync { get; set; }
}
