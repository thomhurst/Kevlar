namespace Kevlar;

/// <summary>Configuration for a retry strategy.</summary>
/// <remarks>
/// Before each retry, callbacks run in this order: <see cref="DelayGenerator"/>,
/// <see cref="DelayGeneratorAsync"/>, <see cref="OnRetry"/>, then <see cref="OnRetryAsync"/>.
/// If the caller's cancellation token is cancelled by the time the callbacks complete, the retry
/// stops and surfaces caller cancellation.
/// </remarks>
public sealed class RetryOptions
{
    /// <summary>
    /// Setting this makes this retry ignore the ambient <c>When…</c> handling clause and handle
    /// only the exceptions this predicate selects.
    /// </summary>
    /// <remarks>
    /// The ambient clause is started with <c>When…</c> on a shield and continued with <c>Or…</c> on
    /// the builder it returns, and applies to every reactive strategy chained after it. This
    /// property replaces that clause for this strategy alone; it does not narrow it.
    /// </remarks>
    /// <seealso cref="HandlingClause"/>
    public Func<Exception, bool>? HandlesException { get; set; }

    /// <summary>Locally handles exceptions using execution context and attempt metadata.</summary>
    public Func<HandlingEvent, bool>? HandlesExceptionWithContext { get; set; }

    internal bool HasHandlingOverride =>
        HandlesException is not null || HandlesExceptionWithContext is not null;

    /// <summary>
    /// The number of <em>retries</em> made after the initial attempt — not the number of attempts.
    /// <c>MaxRetries = 3</c> (the default) makes up to 4 total attempts: the initial call plus
    /// 3 retries. Use <see cref="int.MaxValue"/> to retry forever.
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
    /// The number of <em>retries</em> made after the initial attempt — not the number of attempts.
    /// <c>MaxRetries = 3</c> (the default) makes up to 4 total attempts: the initial call plus
    /// 3 retries. Use <see cref="int.MaxValue"/> to retry forever.
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
    /// Setting this — or <see cref="HandlesResult"/> — makes this retry ignore the ambient
    /// <c>When…</c> handling clause; this predicate then selects the exceptions it handles.
    /// </summary>
    /// <remarks>
    /// The ambient clause is started with <c>When…</c> on a shield and continued with <c>Or…</c> on
    /// the builder it returns, and applies to every reactive strategy chained after it. These
    /// properties replace that clause for this strategy alone; they do not narrow it.
    /// </remarks>
    /// <seealso cref="HandlingClause"/>
    public Func<Exception, bool>? HandlesException { get; set; }

    /// <summary>Locally handles exceptions using the typed outcome and execution context.</summary>
    public Func<HandlingEvent<TResult>, bool>? HandlesExceptionWithContext { get; set; }

    /// <summary>
    /// Setting this — or <see cref="HandlesException"/> — makes this retry ignore the ambient
    /// <c>When…</c> handling clause; this predicate then selects the results it handles.
    /// </summary>
    /// <remarks>
    /// The ambient clause is started with <c>When…</c>/<c>WhenResult…</c> on a shield and continued
    /// with <c>Or…</c> on the builder it returns, and applies to every reactive strategy chained
    /// after it. These properties replace that clause for this strategy alone; they do not narrow it.
    /// </remarks>
    /// <seealso cref="HandlingClause"/>
    public Func<TResult, bool>? HandlesResult { get; set; }

    /// <summary>Locally handles results using the typed outcome and execution context.</summary>
    public Func<HandlingEvent<TResult>, bool>? HandlesResultWithContext { get; set; }

    internal bool HasHandlingOverride =>
        HandlesException is not null
        || HandlesResult is not null
        || HandlesExceptionWithContext is not null
        || HandlesResultWithContext is not null;

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
    private readonly KevlarContext? _context;

    internal RetryEvent(int retryNumber, TimeSpan delay, Exception? exception, object? result, KevlarContext context)
    {
        RetryNumber = retryNumber;
        Delay = delay;
        Exception = exception;
        Result = result;
        _context = context;
    }

    /// <summary>The 1-based number of the retry about to be made (1 = first retry, i.e. second execution).</summary>
    public int RetryNumber { get; }

    /// <summary>The delay that will be waited before the retry.</summary>
    public TimeSpan Delay { get; }

    /// <summary>The exception from the failed attempt, or <see langword="null"/> when a result was handled.</summary>
    public Exception? Exception { get; }

    /// <summary>
    /// The handled result from the failed attempt, or <see langword="null"/> when an exception
    /// occurred.
    /// </summary>
    /// <remarks>
    /// The untyped event carries the result as <see cref="object"/>, so a value-type result is
    /// boxed on every retry and has to be unboxed — and its type re-asserted — by the callback.
    /// Configure the retry through <see cref="RetryOptions{TResult}"/> on a
    /// <see cref="Shield{TResult}"/> to receive <see cref="RetryEvent{TResult}"/> instead: its
    /// <see cref="RetryEvent{TResult}.Outcome"/> is a typed <see cref="Outcome{TResult}"/>, with
    /// no boxing and no cast.
    /// </remarks>
    public object? Result { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it or its property bag after
    /// the callback (including an asynchronous callback) completes.
    /// </summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);
}

/// <summary>Describes a retry that is about to happen, with the handled outcome typed as <typeparamref name="TResult"/>.</summary>
public readonly struct RetryEvent<TResult>
{
    private readonly KevlarContext? _context;

    internal RetryEvent(
        int retryNumber,
        TimeSpan delay,
        Outcome<TResult> outcome,
        KevlarContext context)
    {
        RetryNumber = retryNumber;
        Delay = delay;
        Outcome = outcome;
        _context = context;
    }

    /// <summary>The 1-based number of the retry about to be made (1 = first retry, i.e. second execution).</summary>
    public int RetryNumber { get; }

    /// <summary>The delay that will be waited before the retry.</summary>
    public TimeSpan Delay { get; }

    /// <summary>The handled outcome — the exception or result value that triggered this retry.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>
    /// The ambient execution context. It is pooled; do not retain it or its property bag after
    /// the callback (including an asynchronous callback) completes.
    /// </summary>
    public KevlarContext Context => Internal.EventContext.Required(_context);
}
