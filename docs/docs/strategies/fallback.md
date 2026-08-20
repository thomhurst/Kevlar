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

```csharp
.Fallback(Config.Default,
    onFallback: e => metrics.Increment("config.fallback"))
```

The `FallbackEvent<T>` carries the failure that triggered it as a typed `Outcome<T>` — `Outcome.Exception` when an exception was handled, `Outcome.Result` when a result value was. No casting, no boxing.

## What triggers it

The ambient [handling clause](../handling-failures.md) — exceptions *and* handled results:

```csharp
var http = Shield.For<HttpResponseMessage>()
    .When<HttpRequestException>()
    .WhenResult(r => (int)r.StatusCode >= 500)
    .Fallback((outcome, ct) => new ValueTask<HttpResponseMessage>(CachedResponse()));
```

If the fallback delegate itself throws, that exception becomes the pipeline's outcome — fallbacks don't get fallbacks.

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

Get the order wrong — fallback chained *inside* a retry, hedge or breaker with the same clause — and Kevlar throws at build time instead of silently disabling the outer strategy. See [Composition](../composition.md#impossible-orders-fail-fast).
