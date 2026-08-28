namespace Kevlar;

/// <summary>Configuration for a timeout strategy.</summary>
/// <remarks>
/// <see cref="TimeoutGenerator"/> runs before the timeout timer is armed and overrides
/// <see cref="Timeout"/> for that execution. When a timeout wins, <see cref="OnTimeout"/> runs
/// after timer cleanup and restoration of the caller's cancellation token. Hooks that complete
/// synchronously add no overhead and remain compatible with synchronous <c>Execute</c>.
/// The strategy always awaits the delegate and translates only a direct
/// <see cref="OperationCanceledException"/> into <see cref="TimeoutExceededException"/>. A blocking
/// synchronous delegate that ignores cancellation cannot be timed out, and a wrapped cancellation
/// exception surfaces unchanged.
/// </remarks>
public sealed class TimeoutOptions
{
    /// <summary>An optional low-cardinality name used by strategy telemetry.</summary>
    public string? Name { get; set; }

    /// <summary>The maximum time an execution may take. The default is 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Produces the timeout for each execution and is awaited before the timer is armed. The event
    /// carries the configured <see cref="Timeout"/> and execution context. The returned value must
    /// be positive and no greater than the runtime timer limit. Return <c>new(timeout)</c> from a
    /// synchronous generator. The event context is valid only until the returned task completes.
    /// </summary>
    public Func<TimeoutEvent, ValueTask<TimeSpan>>? TimeoutGenerator { get; set; }

    /// <summary>
    /// Invoked and awaited when an execution is cancelled because it exceeded the timeout. Return
    /// <see langword="default"/> from a synchronous callback. The event context is valid only
    /// until the returned task completes.
    /// </summary>
    public Func<TimeoutEvent, ValueTask>? OnTimeout { get; set; }
}

/// <summary>Provides execution context and duration for timeout callbacks.</summary>
public readonly struct TimeoutEvent
{
    private readonly KevlarContext? _context;

    internal TimeoutEvent(TimeSpan timeout, KevlarContext context)
    {
        Timeout = timeout;
        _context = context;
    }

    /// <summary>
    /// The configured timeout before generation, or the timeout that was exceeded for a timeout
    /// notification.
    /// </summary>
    public TimeSpan Timeout { get; }

    /// <summary>The ambient execution context.</summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);
}
