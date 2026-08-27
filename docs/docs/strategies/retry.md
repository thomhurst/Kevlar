---
sidebar_position: 1
---

# Retry

Re-execute the delegate when it fails, waiting between attempts.

See the [exceptions reference](../exceptions.md) for failures the default retry clause handles.

## Quick forms

```csharp
Shield.Retry(3);                                          // exponential + equal jitter (250ms base, 30s cap)
Shield.Retry(3, Backoff.Constant(TimeSpan.FromSeconds(1)));
Shield.Retry(3, Backoff.Linear(TimeSpan.FromMilliseconds(500)));
Shield.RetryForever(Backoff.Exponential(TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromMinutes(1)));
```

The count is **retries, not attempts**: `Retry(3)` makes up to 4 total attempts — the initial call
plus 3 retries. The same reading applies to `MaxRetries` in the options form, and to every `Retry`
overload on `Shield`, `Shield<T>`, and the `When…` builders.

This executable check removes delays and verifies the count:

<!-- doc-test-run: getting-started-retry-count -->
```csharp
var attempts = 0;
var retryWithoutDelay = Shield.Retry(3, Backoff.None);

try
{
    await retryWithoutDelay.ExecuteAsync(_ =>
    {
        attempts++;
        return ValueTask.FromException(new HttpRequestException("offline"));
    });
}
catch (HttpRequestException)
{
}

if (attempts != 4)
{
    throw new InvalidOperationException($"Expected 4 attempts, observed {attempts}.");
}
```

## Defaults

Bare `Retry(n)` and `RetryForever()` use `Backoff.Default`:

| Setting | Default |
|---|---|
| Curve | Exponential |
| Base delay | 250 ms |
| Factor | 2 |
| Jitter | Equal: each delay is scaled by a value in [0.5, 1.5) |
| Maximum delay | 30 seconds |

The cap also applies to `RetryForever()`. Equal jitter prevents callers from retrying in lockstep.

## Backoff

`Backoff` factories describe the delay sequence:

```csharp
Backoff.Constant(TimeSpan.FromSeconds(1));       // 1s, 1s, 1s, ...
Backoff.Linear(TimeSpan.FromMilliseconds(500));  // 500ms, 1s, 1.5s, ...
Backoff.Exponential(TimeSpan.FromSeconds(1));    // ~1s, ~2s, ~4s, ... (jittered)
Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: Jitter.Full);
Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: Jitter.Decorrelated);
Backoff.Custom(attempt => TimeSpan.FromMilliseconds(100 * attempt));   // attempt is 1-based
_ = Backoff.None;                                // no delay between attempts
_ = Backoff.Default;                             // what bare Retry(n) uses
```

- `Jitter.None` uses the exact curve; `Equal` scales it by [0.5, 1.5); `Full` selects [0, curve); `Decorrelated` selects [base delay, previous delay × 3]. Decorrelated state is isolated per retry execution. Direct backoff consumers can carry the effective preceding delay through `GetDelay(attempt, previousDelay)`; pass zero for the first draw.
- `Exponential(baseDelay, factor = 2.0, maxDelay = null, jitter = Jitter.Equal)` is the default curve. `Backoff.Default` = `Exponential(250ms, maxDelay: 30s)`.
- `Constant(delay, jitter = Jitter.None)` and `Linear(step, maxDelay = null, jitter = Jitter.None)` also accept jitter explicitly.
- Configuration accepts the enum names; legacy Boolean jitter values remain aliases for `Equal` and `None`.
- Without `maxDelay`, built-in and custom backoffs clamp only at the runtime timer limit (`uint.MaxValue - 1` milliseconds, roughly 49.7 days). Use `maxDelay` for an application-specific cap.

## Full options

API reference: [`RetryOptions`](pathname:///api/Kevlar.RetryOptions.html) and [`RetryOptions<T>`](pathname:///api/Kevlar.RetryOptions-1.html).

```csharp
Shield.Retry(o =>
{
    o.MaxRetries = 5;
    o.Backoff = Backoff.Custom(attempt => TimeSpan.FromMilliseconds(100 * attempt));
    o.MaxDelay = TimeSpan.FromSeconds(10);
    o.OnRetry = e =>
    {
        logger.LogWarning(e.Exception, "Retry {AttemptNumber} after {Delay}", e.AttemptNumber, e.Delay);
        return default;
    };
    o.DelayGenerator = e => new(e.AttemptNumber == 0 ? TimeSpan.Zero : null);
    //                       ^ return a TimeSpan to override the computed delay, or null to keep it
});
```

| Option | Default | What it does |
|---|---|---|
| `MaxRetries` | `3` | Retries after the initial attempt — `3` means up to 4 total attempts; `int.MaxValue` = forever |
| `Backoff` | `Backoff.Default` | The delay sequence (see above) |
| `MaxDelay` | backoff cap | Absolute cap applied to every delay, including `DelayGenerator` output. When unset, the backoff's cap applies (30s for `Backoff.Default`); a backoff without a cap falls back to the runtime timer limit |
| `OnRetry` | — | Awaited before each retry sleeps — attempt number, final delay, failure. Return `default` when the work is synchronous |
| `DelayGenerator` | — | Per-retry override returning `ValueTask<TimeSpan?>`: a `TimeSpan` replaces the computed delay, `null` keeps it. This is how [`Retry-After` support](../http.md) works |
| `HandlesException` | — | Local exception predicate; replaces the ambient clause for this retry |
| `HandlesResult` (`RetryOptions<T>`) | — | Local result predicate; replaces the ambient clause together with `HandlesException` |

Invalid option values throw [`KevlarConfigurationException`](../exceptions.md#configuration-failures)
and identify the options type, property, and offending value.

Order per retry: retry metrics are recorded → backoff computes the delay → effective cap (`MaxDelay ?? Backoff.MaxDelay`) clamps it → the awaited `DelayGenerator` may override it → the awaited `OnRetry` sees the final delay and handled outcome → a superseded disposable result is disposed → sleep. The generator's `null` and negative results are ignored, and the effective cap clamps its override.

When result handling triggers another attempt, Kevlar disposes the handled result before the next
attempt starts. It prefers `IAsyncDisposable.DisposeAsync()` when a result implements both disposal
interfaces. `OnRetry` runs first so it can inspect the live result. The final result returned to the
caller is never disposed by the retry strategy. Disposal failures are reported through
`KevlarDiagnostics.OnCallbackError` as `CallbackErrorKind.ResultDisposal` and do not replace the
pipeline outcome.

Both hooks return `ValueTask`. A hook that completes synchronously (`return default;`, `new(value)`)
costs nothing extra and works with synchronous `Execute`. A hook that yields is awaited by
`ExecuteAsync`; reached through synchronous `Execute`, it throws `NotSupportedException` at that
call. See [synchronous execution compatibility](../executing.md#synchronous-execution-compatibility).
Notification-hook exceptions follow the shared [callback-failure contract](../observability.md#callback-failures):
they are reported and never replace the protected outcome.

`RetryOptions` and `RetryOptions<T>` are standalone sibling types. Both expose the same
`MaxRetries`, `Backoff`, and `MaxDelay` settings, while their callback properties use distinct
`RetryEvent` and `RetryEvent<T>` delegates. Configure shared scalar defaults in each options
lambda; a `RetryOptions<T>` instance is not assignable to `RetryOptions`.

Generators receive the caller token through `e.Context.CancellationToken`. If cancellation
arrives while a generator is awaiting, notification hooks still run after it completes, then the
next attempt is suppressed and caller cancellation surfaces. A generator exception surfaces with
its original identity and skips later hooks. `RetryEvent.Context` is pooled execution state: use it
only before the returned `ValueTask` completes; never retain it or its property bag.

On an untyped `Shield`, the `RetryEvent` callbacks receive: `AttemptNumber` (zero-based, so `0` is the first retry after the initial execution), `Delay`, `Exception` (null when a handled *result* triggered the retry), `Result` (the handled result, boxed as `object?`) and `Context`. On a typed `Shield<T>`, the events are `RetryEvent<T>` instead: same `AttemptNumber`/`Delay`/`Context`, plus the handled failure as a directly stored typed `Outcome<T>` — `e.Outcome.Result` is your `T`, with no boxing, reconstruction, or cast.

<!-- doc-test-run: retry-numbers -->
```csharp
var attemptNumbers = new List<int>();
var numberedRetry = Shield.Retry(options =>
{
    options.MaxRetries = 3;
    options.Backoff = Backoff.None;
    options.OnRetry = retry =>
    {
        attemptNumbers.Add(retry.AttemptNumber);
        return default; // completes synchronously, so synchronous Execute below is fine
    };
});

try
{
    numberedRetry.Execute(static _ => throw new InvalidOperationException());
}
catch (InvalidOperationException)
{
}

Console.WriteLine(string.Join(",", attemptNumbers)); // 0,1,2
```

## What gets retried

Whatever the current [handling clause](../handling-failures.md) says is a failure—ordinary
exceptions under the default, or the outcomes selected by your `When`/`WhenResult` clause:

```csharp
Shield
    .When<HttpRequestException>()
    .Or<TimeoutExceededException>()
    .Retry(5);
```

The options-lambda form can instead set `HandlesException` and, on `Shield<T>`, `HandlesResult`.
Setting either creates a [per-strategy override](../handling-failures.md#per-strategy-overrides);
unspecified outcome kinds are not handled.

## Placement in the chain

```csharp
Shield
    .Timeout(TimeSpan.FromSeconds(30))   // total budget: retries must fit inside
    .Retry(3)
    .Timeout(TimeSpan.FromSeconds(5));   // each attempt gets 5s, and Retry sees the TimeoutExceededException
```

Retry outside a per-attempt timeout retries timeouts; retry inside a circuit breaker hammers a struggling dependency before the breaker sees the pattern. The [composition rules](../composition.md) cover this in depth.
