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

:::tip The default is the good one
Bare `Shield.Retry(3)` gives exponential backoff **with equal jitter**, 250ms base, capped at 30s. Jitter prevents retry storms where every failed caller retries in lockstep; you'd have configured this anyway.
:::

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

- `Jitter.None` uses the exact curve; `Equal` scales it by [0.5, 1.5); `Full` selects [0, curve); `Decorrelated` selects [initial delay, previous delay × 3]. Decorrelated state is isolated per retry execution. Direct backoff consumers can carry the effective preceding delay through `GetDelay(attempt, previousDelay)`; pass zero for the first draw.
- `Exponential(initialDelay, factor = 2.0, maxDelay = null, jitter = Jitter.Equal)` is the default curve. `Backoff.Default` = `Exponential(250ms, maxDelay: 30s)`.
- `Constant(delay, jitter = Jitter.None)` and `Linear(step, maxDelay = null, jitter = Jitter.None)` also accept jitter explicitly.
- Configuration accepts the enum names; legacy Boolean jitter values remain aliases for `Equal` and `None`.
- Without `maxDelay`, built-in and custom backoffs clamp only at the runtime timer limit (`uint.MaxValue - 1` milliseconds, roughly 49.7 days). Use `maxDelay` for an application-specific cap.

## Full options

```csharp
Shield.Retry(o =>
{
    o.MaxRetries = 5;
    o.Backoff = Backoff.Custom(attempt => TimeSpan.FromMilliseconds(100 * attempt));
    o.MaxDelay = TimeSpan.FromSeconds(10);
    o.OnRetry = e => logger.LogWarning(e.Exception,
        "Retry {RetryNumber} after {Delay}", e.RetryNumber, e.Delay);
    o.DelayGenerator = e => /* return a TimeSpan to override the computed delay, or null */ null;
    o.DelayGeneratorAsync = e => new ValueTask<TimeSpan?>(TimeSpan.Zero);
});
```

| Option | Default | What it does |
|---|---|---|
| `MaxRetries` | `3` | Retries after the initial attempt — `3` means up to 4 total attempts; `int.MaxValue` = forever |
| `Backoff` | `Backoff.Default` | The delay sequence (see above) |
| `MaxDelay` | — | Absolute cap applied to every delay — including `DelayGenerator` output, so a hostile `Retry-After` can't stall the pipeline |
| `OnRetry` | — | Called synchronously before each retry sleeps — attempt number, final delay, failure |
| `OnRetryAsync` | — | Awaited before each retry sleeps |
| `DelayGenerator` | — | Per-retry override: return a `TimeSpan` to replace the computed delay, or `null` to keep it. This is how [`Retry-After` support](../http.md) works |
| `DelayGeneratorAsync` | — | Awaited per-retry override for asynchronous delay sources; return a `TimeSpan` to replace the current delay, or `null` to keep it |
| `HandlesException` | — | Local exception predicate; replaces the ambient clause for this retry |
| `HandlesResult` (`RetryOptions<T>`) | — | Local result predicate; replaces the ambient clause together with `HandlesException` |

Order per retry: retry metrics are recorded → backoff computes the delay → `MaxDelay` clamps it → `DelayGenerator` may override it → the awaited `DelayGeneratorAsync` may override that result → `OnRetry`/`OnRetryAsync` see the final delay → sleep. Both generators ignore `null` and negative results, and `MaxDelay` clamps each override.

`RetryOptions` and `RetryOptions<T>` are standalone sibling types. Both expose the same
`MaxRetries`, `Backoff`, and `MaxDelay` settings, while their callback properties use distinct
`RetryEvent` and `RetryEvent<T>` delegates. Configure shared scalar defaults in each options
lambda; a `RetryOptions<T>` instance is not assignable to `RetryOptions`.

Async generators receive the caller token through `e.Context.CancellationToken`. If cancellation
arrives while a generator is awaiting, notification hooks still run after it completes, then the
next attempt is suppressed and caller cancellation surfaces. A generator exception surfaces with
its original identity and skips later hooks. `RetryEvent.Context` is pooled execution state: use it
only before the returned `ValueTask` completes; never retain it or its property bag.

On an untyped `Shield`, the `RetryEvent` callbacks receive: `RetryNumber` (1-based, so `1` is the first retry after the initial execution), `Delay`, `Exception` (null when a handled *result* triggered the retry), `Result` (the handled result, boxed as `object?`) and `Context`. On a typed `Shield<T>`, the events are `RetryEvent<T>` instead: same `RetryNumber`/`Delay`/`Context`, plus the handled failure as a directly stored typed `Outcome<T>` — `e.Outcome.Result` is your `T`, with no boxing, reconstruction, or cast.

<!-- doc-test-run: retry-numbers -->
```csharp
var retryNumbers = new List<int>();
var numberedRetry = Shield.Retry(options =>
{
    options.MaxRetries = 3;
    options.Backoff = Backoff.None;
    options.OnRetry = retry => retryNumbers.Add(retry.RetryNumber);
});

try
{
    numberedRetry.Execute(static _ => throw new InvalidOperationException());
}
catch (InvalidOperationException)
{
}

Console.WriteLine(string.Join(",", retryNumbers)); // 1,2,3
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
