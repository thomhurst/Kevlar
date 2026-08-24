---
sidebar_position: 7
---

# Fallback

When all else fails, degrade gracefully instead of throwing: return a default, a cached value, or anything you can compute from the failure.

Result-producing fallbacks live on **typed** shields — reach one via `Shield.For<T>()`:

```csharp
var shield = Shield.For<Config>()
    .When<HttpRequestException>()
    .Fallback(Config.Default);
```

Void executions get their own fallback on the plain `Shield` — run an alternative action instead of producing a value:

```csharp
var shield = Shield
    .When<MessagingException>()
    .Fallback((exception, ct) => deadLetter.PublishAsync(exception, ct));

await shield.ExecuteAsync(ct => bus.PublishAsync(message, ct));
```

`Shield.Fallback(…)` is the static factory form, for when the fallback is the outermost strategy —
which is the valid position for one, since it recovers what everything inside it could not:

```csharp
var shield = Shield
    .Fallback((exception, ct) => deadLetter.PublishAsync(exception, ct))
    .Retry(3);
```

A void fallback changes the chain's type to `VoidShield`. That type exposes only void execution
overloads, and later fluent methods keep returning `VoidShield`, so a result-returning execution is
rejected by the compiler. It also cannot be lifted with `For<TResult>()`, wrapped into a typed
shield, or passed to mixed `Shield.Compose(...)`. Use `Wrap(...)` when composing around a void
shield; the result remains `VoidShield`.

```csharp
var shield = Shield.Retry(3)
    .Fallback(static _ => ValueTask.CompletedTask)
    .Timeout(TimeSpan.FromSeconds(10)); // still VoidShield

await shield.ExecuteAsync(static _ => ValueTask.CompletedTask); // valid
// shield.ExecuteAsync(static _ => new ValueTask<int>(42));      // does not compile
```

## Three shapes

<!-- doc-test-ignore: Alternative fluent fragments require the typed builder introduced by the surrounding prose. -->
```csharp
// 1. A constant value:
.Fallback(Config.Default)

// 2. Computed (async), no failure context needed:
.Fallback(ct => new ValueTask<Config>(cache.Get()))

// 3. Computed with access to the failure:
.Fallback((outcome, ct) =>
{
    logger.LogError(outcome.Exception, "Using cached config");
    return new ValueTask<Config>(cache.Get());
})
```

Every overload also has an options configurator for fallback notifications:

<!-- doc-test-ignore: Fluent fragment requires the typed builder introduced by the surrounding prose. -->
```csharp
.Fallback(Config.Default,
    options => options.OnFallback = e => metrics.Increment("config.fallback"))
```

Each shape has exactly two overloads — bare and `configure`. There is no positional `onFallback`
parameter: assign the callback to `options.OnFallback` as shown above.

The `FallbackEvent<T>` carries the failure that triggered it as a typed `Outcome<T>` — `Outcome.Exception` when an exception was handled, `Outcome.Result` when a result value was. No casting, no boxing.

`OnFallback` runs before the fallback value or factory. If the callback throws, its exact
exception becomes the pipeline outcome and the factory is not called. A later execution is
unaffected. Async factory failures are likewise preserved as the pipeline outcome.

## Notifications

Configure both synchronous and awaited notification work in the same lambda:

<!-- doc-test-ignore: Illustrative logger and audit dependencies are application services. -->
```csharp
var shield = Shield.For<Config>()
    .When<HttpRequestException>()
    .Fallback(
        Config.Default,
        options =>
        {
            options.OnFallback = e => logger.LogWarning(e.Outcome.Exception, "Using defaults");
            options.OnFallbackAsync = async e =>
                await audit.RecordFallbackAsync(e.Outcome, e.Context.CancellationToken);
        });
```

Kevlar records its fallback metric, invokes `OnFallback`, awaits `OnFallbackAsync`, then runs
the fallback value or factory. A notification exception or cancellation is preserved as the exact
pipeline outcome and skips recovery. Caller cancellation is exposed through `e.Context` and the
token passed to the recovery factory; Kevlar does not forcibly stop either callback when user code
chooses not to observe that token.

`FallbackOptions<T>` preserves typed outcomes. Plain `Shield` uses `FallbackOptions` and a
non-generic `FallbackEvent` carrying the exact handled exception. Callback properties are
snapshotted when the shield is built, so replacing them on the options later has no effect.

Both option types expose `HandlesException`; `FallbackOptions<T>` also exposes `HandlesResult`.
Setting either creates a [per-strategy override](../handling-failures.md#per-strategy-overrides)
that fully replaces the ambient clause for this fallback. A result-only override does not recover
exceptions, and an exception-only override does not recover results.

Hooks may run concurrently when the same shield executes concurrently, and may re-enter the shield;
they must therefore be thread-safe and must not depend on strategy locks. `FallbackEvent.Context`
remains valid until that hook returns or its `ValueTask` completes. Do not retain the pooled context
or use it from background work after completion.

## What triggers it

The ambient [handling clause](../handling-failures.md) — exceptions *and* handled results:

```csharp
var http = Shield.For<HttpResponseMessage>()
    .When<HttpRequestException>()
    .OrResult(r => (int)r.StatusCode >= 500)
    .Fallback((outcome, ct) => new ValueTask<HttpResponseMessage>(CachedResponse()));
```

If the fallback delegate itself throws, that exception becomes the pipeline's outcome — fallbacks don't get fallbacks.

As with every reactive strategy, the default clause bypasses `OperationCanceledException`.
Cancellation can be recovered explicitly when that is intentional:

```csharp
var shield = Shield.For<Config>()
    .When<OperationCanceledException>()
    .Fallback(Config.Default);
```

Async typed and void fallback delegates receive the active `CancellationToken`. Caller
cancellation therefore reaches a fallback that is already running. If a timeout is outside the
fallback, the delegate receives the timeout scope's token and must observe it to stay within the
total budget. If the fallback is outside the timeout, timeout cleanup completes first and the
fallback receives the restored caller token.

## Placement

Fallback is usually **outermost** — the last line of defence after retries and breakers have given up:

```csharp
var shield = Shield.For<Quote>()
    .When<HttpRequestException>()
    .Or<CircuitOpenException>()      // catch the breaker's rejection too
    .Fallback(Quote.Unavailable)
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
```

Note the clause: to fall back when the circuit is open, the fallback's handling clause must include `CircuitOpenException`.

The same outermost rule applies to hedging: an outer fallback runs once after every hedge attempt
has produced a handled outcome. A fallback inside a hedge with the same clause is rejected because
it would recover each attempt before the hedge could launch another one.

Get the order wrong — fallback chained *inside* a retry, hedge or breaker with the same clause — and Kevlar throws at build time instead of silently disabling the outer strategy. See [Composition](../composition.md#impossible-orders-fail-fast).
