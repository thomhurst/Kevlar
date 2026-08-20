namespace Kevlar;

/// <summary>Configuration for a retry strategy.</summary>
public sealed class RetryOptions
{
    /// <summary>
    /// Maximum number of retries after the initial attempt. The default is 3
    /// (up to 4 total attempts). Use <see cref="int.MaxValue"/> to retry forever.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>The delay computation between attempts. Defaults to <see cref="Backoff.Default"/>.</summary>
    public Backoff Backoff { get; set; } = Backoff.Default;

    /// <summary>An absolute upper bound applied to every computed delay.</summary>
    public TimeSpan? MaxDelay { get; set; }

    /// <summary>Invoked synchronously before each retry sleeps.</summary>
    public Action<RetryEvent>? OnRetry { get; set; }

    /// <summary>Invoked and awaited before each retry sleeps.</summary>
    public Func<RetryEvent, ValueTask>? OnRetryAsync { get; set; }

    /// <summary>
    /// Overrides the computed delay for a specific retry. Receives the event with the
    /// backoff-computed delay; return a non-null value to replace it (for example, from an
    /// HTTP <c>Retry-After</c> header via <see cref="RetryEvent.Result"/>).
    /// </summary>
    public Func<RetryEvent, TimeSpan?>? DelayGenerator { get; set; }
}

/// <summary>Describes a retry that is about to happen.</summary>
public readonly struct RetryEvent
{
    internal RetryEvent(int attempt, TimeSpan delay, Exception? exception, object? result, KevlarContext context)
    {
        Attempt = attempt;
        Delay = delay;
        Exception = exception;
        Result = result;
        Context = context;
    }

    /// <summary>The 1-based number of the retry about to be made.</summary>
    public int Attempt { get; }

    /// <summary>The delay that will be waited before the retry.</summary>
    public TimeSpan Delay { get; }

    /// <summary>The exception from the failed attempt, or <see langword="null"/> when a result was handled.</summary>
    public Exception? Exception { get; }

    /// <summary>The handled result from the failed attempt (boxed), or <see langword="null"/> when an exception occurred.</summary>
    public object? Result { get; }

    /// <summary>The ambient execution context.</summary>
    public KevlarContext Context { get; }
}
