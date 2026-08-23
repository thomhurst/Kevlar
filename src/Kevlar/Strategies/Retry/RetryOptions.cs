namespace Kevlar;

/// <summary>Configuration for a retry strategy.</summary>
/// <remarks>
/// Before each retry, callbacks run in this order: <see cref="DelayGenerator"/>,
/// <see cref="DelayGeneratorAsync"/>, <see cref="OnRetry"/>, then <see cref="OnRetryAsync"/>.
/// If the caller's cancellation token is cancelled by the time the callbacks complete, the retry
/// stops and surfaces caller cancellation.
/// </remarks>
public class RetryOptions
{
    /// <summary>
    /// Locally selects exceptions handled by this retry. When set, this strategy ignores the
    /// ambient handling clause and handles only outcomes selected by its local predicates.
    /// </summary>
    public Func<Exception, bool>? HandlesException { get; set; }

    internal bool HasHandlingOverride => HandlesException is not null;

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

    /// <summary>
    /// Asynchronously overrides the delay for a specific retry. Receives the delay after
    /// <see cref="DelayGenerator"/> and <see cref="MaxDelay"/> have been applied; return a
    /// non-null value to replace it. <see cref="MaxDelay"/> still caps the returned value.
    /// The callback is awaited before retry notifications run. Do not retain its pooled
    /// <see cref="RetryEvent.Context"/> after the returned task completes.
    /// </summary>
    public Func<RetryEvent, ValueTask<TimeSpan?>>? DelayGeneratorAsync { get; set; }
}

/// <summary>
/// Result-typed configuration for a retry strategy on a <see cref="Shield{TResult}"/>: the events
/// carry a typed <see cref="Outcome{TResult}"/> instead of a boxed <see cref="object"/> result.
/// </summary>
/// <remarks>
/// <see cref="RetryOptions{TResult}"/> and <see cref="RetryOptions"/> are standalone sibling types.
/// Their callback properties expose distinct delegate types and preserve the delegates assigned
/// to them.
/// Before each retry, callbacks run in this order: <see cref="DelayGenerator"/>,
/// <see cref="DelayGeneratorAsync"/>, <see cref="OnRetry"/>, then <see cref="OnRetryAsync"/>.
/// If the caller's cancellation token is cancelled by the time the callbacks complete, the retry
/// stops and surfaces caller cancellation.
/// </remarks>
public sealed class RetryOptions<TResult>
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

    /// <summary>
    /// Locally selects exceptions handled by this retry. When either local predicate is set, this
    /// strategy ignores the ambient handling clause and handles only outcomes selected locally.
    /// </summary>
    public Func<Exception, bool>? HandlesException { get; set; }

    /// <summary>
    /// Locally selects results handled by this retry. When either local predicate is set, this
    /// strategy ignores the ambient handling clause and handles only outcomes selected locally.
    /// </summary>
    public Func<TResult, bool>? HandlesResult { get; set; }

    internal bool HasHandlingOverride =>
        HandlesException is not null || HandlesResult is not null;

    /// <summary>Invoked synchronously before each retry sleeps, with the typed handled outcome.</summary>
    public Action<RetryEvent<TResult>>? OnRetry { get; set; }

    /// <summary>Invoked and awaited before each retry sleeps, with the typed handled outcome.</summary>
    public Func<RetryEvent<TResult>, ValueTask>? OnRetryAsync { get; set; }

    /// <summary>
    /// Overrides the computed delay for a specific retry, with the typed handled outcome.
    /// Return a non-null value to replace the backoff-computed delay;
    /// <see cref="MaxDelay"/> still caps the returned value.
    /// </summary>
    public Func<RetryEvent<TResult>, TimeSpan?>? DelayGenerator { get; set; }

    /// <summary>
    /// Asynchronously overrides the delay for a specific retry, with the typed handled outcome.
    /// Receives the delay after the synchronous generator and <see cref="MaxDelay"/>
    /// have been applied. Return a non-null value to replace it; the maximum still applies.
    /// </summary>
    public Func<RetryEvent<TResult>, ValueTask<TimeSpan?>>? DelayGeneratorAsync { get; set; }
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

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it or its property bag after
    /// the callback (including an asynchronous callback) completes.
    /// </summary>
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

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it or its property bag after
    /// the callback (including an asynchronous callback) completes.
    /// </summary>
    public KevlarContext Context => _inner.Context;
}
