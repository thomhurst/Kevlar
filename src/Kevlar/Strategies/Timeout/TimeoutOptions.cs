namespace Kevlar;

/// <summary>Configuration for a timeout strategy.</summary>
public sealed class TimeoutOptions
{
    /// <summary>The maximum time an execution may take. The default is 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Invoked when an execution is cancelled because it exceeded the timeout.</summary>
    public Action<TimeoutEvent>? OnTimeout { get; set; }
}

/// <summary>Describes an execution that exceeded its timeout.</summary>
public readonly struct TimeoutEvent
{
    internal TimeoutEvent(TimeSpan timeout, KevlarContext context)
    {
        Timeout = timeout;
        Context = context;
    }

    /// <summary>The timeout that was exceeded.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>The ambient execution context.</summary>
    public KevlarContext Context { get; }
}
