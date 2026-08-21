namespace Kevlar;

/// <summary>Configuration for a retry strategy.</summary>
/// <remarks>
/// Before each retry, callbacks run in this order: <see cref="DelayGenerator"/>,
/// <see cref="OnRetry"/>, then <see cref="OnRetryAsync"/>. If the caller's cancellation token is
/// cancelled by the time the callbacks complete, the retry stops and surfaces caller cancellation.
/// </remarks>
public class RetryOptions
{
    /// <summary>
    /// Maximum number of retries after the initial attempt. The default is 3
    /// (up to 4 total executions). Use <see cref="int.MaxValue"/> to retry forever.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>The delay computation between attempts. Defaults to <see cref="Backoff.Default"/>.</summary>
    public Backoff Backoff { get; set; } = Backoff.Default;

    /// <summary>
    /// An absolute upper bound applied to every delay, including delays produced by
    /// <see cref="DelayGenerator"/> (so a huge <c>Retry-After</c> header cannot stall the pipeline).
    /// </summary>
    public TimeSpan? MaxDelay { get; set; }

    /// <summary>Invoked synchronously before each retry sleeps.</summary>
    public Action<RetryEvent>? OnRetry { get; set; }

    /// <summary>Invoked and awaited before each retry sleeps.</summary>
    public Func<RetryEvent, ValueTask>? OnRetryAsync { get; set; }

    /// <summary>
    /// Overrides the computed delay for a specific retry. Receives the event with the
    /// backoff-computed delay; return a non-null value to replace it (for example, from an
    /// HTTP <c>Retry-After</c> header). <see cref="MaxDelay"/> still caps the returned value.
    /// </summary>
    public Func<RetryEvent, TimeSpan?>? DelayGenerator { get; set; }
}

/// <summary>
/// Result-typed configuration for a retry strategy on a <see cref="Shield{TResult}"/>: the events
/// carry a typed <see cref="Outcome{TResult}"/> instead of a boxed <see cref="object"/> result.
/// </summary>
public sealed class RetryOptions<TResult> : RetryOptions
{
    private Action<RetryEvent<TResult>>? _onRetry;
    private Func<RetryEvent<TResult>, ValueTask>? _onRetryAsync;
    private Func<RetryEvent<TResult>, TimeSpan?>? _delayGenerator;

    /// <summary>Invoked synchronously before each retry sleeps, with the typed handled outcome.</summary>
    public new Action<RetryEvent<TResult>>? OnRetry
    {
        get => _onRetry;
        set
        {
            _onRetry = value;
            base.OnRetry = value is null ? null : untyped => value(new RetryEvent<TResult>(untyped));
        }
    }

    /// <summary>Invoked and awaited before each retry sleeps, with the typed handled outcome.</summary>
    public new Func<RetryEvent<TResult>, ValueTask>? OnRetryAsync
    {
        get => _onRetryAsync;
        set
        {
            _onRetryAsync = value;
            base.OnRetryAsync = value is null ? null : untyped => value(new RetryEvent<TResult>(untyped));
        }
    }

    /// <summary>
    /// Overrides the computed delay for a specific retry, with the typed handled outcome.
    /// Return a non-null value to replace the backoff-computed delay;
    /// <see cref="RetryOptions.MaxDelay"/> still caps the returned value.
    /// </summary>
    public new Func<RetryEvent<TResult>, TimeSpan?>? DelayGenerator
    {
        get => _delayGenerator;
        set
        {
            _delayGenerator = value;
            base.DelayGenerator = value is null ? null : untyped => value(new RetryEvent<TResult>(untyped));
        }
    }
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

    /// <summary>The 1-based number of the retry about to be made (1 = first retry, i.e. second execution).</summary>
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

/// <summary>Describes a retry that is about to happen, with the handled outcome typed as <typeparamref name="TResult"/>.</summary>
public readonly struct RetryEvent<TResult>
{
    private readonly RetryEvent _inner;

    internal RetryEvent(RetryEvent inner) => _inner = inner;

    /// <summary>The 1-based number of the retry about to be made (1 = first retry, i.e. second execution).</summary>
    public int Attempt => _inner.Attempt;

    /// <summary>The delay that will be waited before the retry.</summary>
    public TimeSpan Delay => _inner.Delay;

    /// <summary>The handled outcome — the exception or result value that triggered this retry.</summary>
    public Outcome<TResult> Outcome =>
        _inner.Exception is { } exception
            ? Outcome<TResult>.FromException(exception)
            : Outcome<TResult>.FromResult((TResult)_inner.Result!);

    /// <summary>The ambient execution context.</summary>
    public KevlarContext Context => _inner.Context;
}
