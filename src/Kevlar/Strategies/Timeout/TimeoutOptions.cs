namespace Kevlar;

/// <summary>Configuration for a timeout strategy.</summary>
/// <remarks>
/// <see cref="TimeoutGenerator"/> runs before the timeout timer is armed and overrides
/// <see cref="Timeout"/> for that execution. When a timeout wins, callbacks run in this order:
/// <see cref="OnTimeout"/>, then <see cref="OnTimeoutAsync"/>. Both callbacks run after timer
/// cleanup and restoration of the caller's cancellation token.
/// </remarks>
public sealed class TimeoutOptions
{
    /// <summary>The maximum time an execution may take. The default is 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Produces and awaits the timeout for each execution. The returned value must be positive and
    /// no greater than the runtime timer limit. The context is valid only until the callback completes.
    /// </summary>
    public Func<KevlarContext, ValueTask<TimeSpan>>? TimeoutGenerator { get; set; }

    /// <summary>Invoked when an execution is cancelled because it exceeded the timeout.</summary>
    public Action<TimeoutEvent>? OnTimeout { get; set; }

    /// <summary>
    /// Invoked and awaited after <see cref="OnTimeout"/> when an execution exceeds its timeout.
    /// The event context is valid only until the callback completes.
    /// </summary>
    public Func<TimeoutEvent, ValueTask>? OnTimeoutAsync { get; set; }
}

/// <summary>Describes an execution that exceeded its timeout.</summary>
public readonly struct TimeoutEvent
{
    private readonly KevlarContext? _context;

    internal TimeoutEvent(TimeSpan timeout, KevlarContext context)
    {
        Timeout = timeout;
        _context = context;
    }

    /// <summary>The timeout that was exceeded.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>The ambient execution context.</summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);
}
