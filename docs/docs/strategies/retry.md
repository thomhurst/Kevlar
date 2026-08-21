---
sidebar_position: 1
---

# Retry

Re-execute the delegate when it fails, waiting between attempts.

## Quick forms

```csharp
Shield.Retry(3);                                          // exponential + jitter (250ms base, 30s cap)
Shield.Retry(3, Backoff.Constant(TimeSpan.FromSeconds(1)));
Shield.Retry(3, Backoff.Linear(TimeSpan.FromMilliseconds(500)));
Shield.RetryForever(Backoff.Exponential(TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromMinutes(1)));
```

`Retry(3)` means **3 retries** — up to 4 total attempts.

:::tip The default is the good one
Bare `Shield.Retry(3)` gives exponential backoff **with jitter**, 250ms base, capped at 30s. Jitter prevents retry storms where every failed caller retries in lockstep; you'd have configured this anyway.
:::

## Backoff

`Backoff` factories describe the delay sequence:

```csharp
Backoff.Constant(TimeSpan.FromSeconds(1));       // 1s, 1s, 1s, ...
Backoff.Linear(TimeSpan.FromMilliseconds(500));  // 500ms, 1s, 1.5s, ...
Backoff.Exponential(TimeSpan.FromSeconds(1));    // ~1s, ~2s, ~4s, ... (jittered)
Backoff.Custom(attempt => TimeSpan.FromMilliseconds(100 * attempt));   // attempt is 1-based
_ = Backoff.None;                                // no delay between attempts
_ = Backoff.Default;                             // what bare Retry(n) uses
```

- `Exponential(initialDelay, factor = 2.0, maxDelay = null, jitter = true)` — jitter scales each delay by a random factor in [0.5, 1.5) to avoid synchronized retry storms. `Backoff.Default` = `Exponential(250ms, maxDelay: 30s)`.
- `Linear(step, maxDelay = null)` — step × attempt, no jitter.
- When you don't pass a `maxDelay`, built-in backoffs still clamp at 1 day.

## Full options

```csharp
Shield.Retry(o =>
{
    o.MaxRetries = 5;
    o.Backoff = Backoff.Custom(attempt => TimeSpan.FromMilliseconds(100 * attempt));
    o.MaxDelay = TimeSpan.FromSeconds(10);
    o.OnRetry = e => logger.LogWarning(e.Exception,
        "Retry {Attempt} after {Delay}", e.Attempt, e.Delay);
    o.DelayGenerator = e => /* return a TimeSpan to override the computed delay, or null */ null;
    o.DelayGeneratorAsync = e => new ValueTask<TimeSpan?>(TimeSpan.Zero);
});
```

| Option | Default | What it does |
|---|---|---|
| `MaxRetries` | `3` | Retry attempts after the initial one; `int.MaxValue` = forever |
| `Backoff` | `Backoff.Default` | The delay sequence (see above) |
| `MaxDelay` | — | Absolute cap applied to every delay — including `DelayGenerator` output, so a hostile `Retry-After` can't stall the pipeline |
| `OnRetry` | — | Called synchronously before each retry sleeps — attempt number, final delay, failure |
| `OnRetryAsync` | — | Awaited before each retry sleeps |
| `DelayGenerator` | — | Per-retry override: return a `TimeSpan` to replace the computed delay, or `null` to keep it. This is how [`Retry-After` support](../http.md) works |
| `DelayGeneratorAsync` | — | Awaited per-retry override for asynchronous delay sources; return a `TimeSpan` to replace the current delay, or `null` to keep it |

Order per retry: retry metrics are recorded → backoff computes the delay → `MaxDelay` clamps it → `DelayGenerator` may override it → the awaited `DelayGeneratorAsync` may override that result → `OnRetry`/`OnRetryAsync` see the final delay → sleep. Both generators ignore `null` and negative results, and `MaxDelay` clamps each override.

Async generators receive the caller token through `e.Context.CancellationToken`. If cancellation
arrives while a generator is awaiting, notification hooks still run after it completes, then the
next attempt is suppressed and caller cancellation surfaces. A generator exception surfaces with
its original identity and skips later hooks. `RetryEvent.Context` is pooled execution state: use it
only before the returned `ValueTask` completes; never retain it or its property bag.

On an untyped `Shield`, the `RetryEvent` callbacks receive: `Attempt` (1-based retry number), `Delay`, `Exception` (null when a handled *result* triggered the retry), `Result` (the handled result, boxed as `object?`) and `Context`. On a typed `Shield<T>`, the events are `RetryEvent<T>` instead: same `Attempt`/`Delay`/`Context`, plus the handled failure as a typed `Outcome<T>` — `e.Outcome.Result` is your `T`, no casting.

## What gets retried

Whatever the current [handling clause](../handling-failures.md) says is a failure — by default any exception except `OperationCanceledException`, or your `When`/`WhenResult` clause:

```csharp
Shield
    .When<HttpRequestException>()
    .Or<TimeoutExceededException>()
    .Retry(5);
```

## Placement in the chain

```csharp
Shield
    .Timeout(TimeSpan.FromSeconds(30))   // total budget: retries must fit inside
    .Retry(3)
    .Timeout(TimeSpan.FromSeconds(5));   // each attempt gets 5s, and Retry sees the TimeoutExceededException
```

Retry outside a per-attempt timeout retries timeouts; retry inside a circuit breaker hammers a struggling dependency before the breaker sees the pattern. The [composition rules](../composition.md) cover this in depth.
