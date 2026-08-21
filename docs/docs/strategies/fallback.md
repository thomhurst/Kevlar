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

A void fallback guards void executions only; executing a result-returning delegate through it fails with a descriptive error rather than inventing a default value.

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

Every overload takes an optional `onFallback` callback:

<!-- doc-test-ignore: Fluent fragment requires the typed builder introduced by the surrounding prose. -->
```csharp
.Fallback(Config.Default,
    onFallback: e => metrics.Increment("config.fallback"))
```

The `FallbackEvent<T>` carries the failure that triggered it as a typed `Outcome<T>` — `Outcome.Exception` when an exception was handled, `Outcome.Result` when a result value was. No casting, no boxing.

`onFallback` runs before the fallback value or factory. If the callback throws, its exact
exception becomes the pipeline outcome and the factory is not called. A later execution is
unaffected. Async factory failures are likewise preserved as the pipeline outcome.

## What triggers it

The ambient [handling clause](../handling-failures.md) — exceptions *and* handled results:

```csharp
var http = Shield.For<HttpResponseMessage>()
    .When<HttpRequestException>()
    .WhenResult(r => (int)r.StatusCode >= 500)
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
    .When<CircuitOpenException>()    // catch the breaker's rejection too
    .Fallback(Quote.Unavailable)
    .Retry(3)
    .CircuitBreaker(5, TimeSpan.FromSeconds(30));
```

Note the clause: to fall back when the circuit is open, the fallback's handling clause must include `CircuitOpenException`.

The same outermost rule applies to hedging: an outer fallback runs once after every hedge attempt
has produced a handled outcome. A fallback inside a hedge with the same clause is rejected because
it would recover each attempt before the hedge could launch another one.

Get the order wrong — fallback chained *inside* a retry, hedge or breaker with the same clause — and Kevlar throws at build time instead of silently disabling the outer strategy. See [Composition](../composition.md#impossible-orders-fail-fast).
