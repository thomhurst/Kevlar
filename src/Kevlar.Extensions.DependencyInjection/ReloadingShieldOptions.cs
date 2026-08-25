namespace Kevlar.Extensions.DependencyInjection;

/// <summary>Controls configuration-change handling for a reloading shield.</summary>
public sealed class ReloadingShieldOptions
{
    /// <summary>
    /// Gets or sets how long configuration changes are coalesced before rebuilding the shield.
    /// </summary>
    public TimeSpan DebounceDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets or sets the clock used to schedule reloads.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
