---
sidebar_position: 7
---

# Fallback

When all else fails, degrade gracefully instead of throwing: return a default, a cached value, or anything you can compute from the failure.

Fallback produces a *result*, so it lives on **typed** policies only — reach one via `Policy.For<T>()`:

```csharp
var policy = Policy.For<Config>()
    .Handle<HttpRequestException>()
    .Fallback(Config.Default);
```

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

The `FallbackEvent` carries the failure that triggered it: `Exception` (when an exception was handled) or `Result` (when a handled result triggered it — boxed as `object?`, cast it back).

## What triggers it

The ambient [handling clause](../handling-failures.md) — exceptions *and* handled results:

```csharp
var http = Policy.For<HttpResponseMessage>()
    .Handle<HttpRequestException>()
    .HandleResult(r => (int)r.StatusCode >= 500)
    .Fallback((outcome, ct) => new ValueTask<HttpResponseMessage>(CachedResponse()));
```

If the fallback delegate itself throws, that exception becomes the pipeline's outcome — fallbacks don't get fallbacks.

## Placement

Fallback is usually **outermost** — the last line of defence after retries and breakers have given up:

```csharp
var policy = Policy.For<Quote>()
    .Handle<HttpRequestException>()
    .Handle<CircuitOpenException>()    // catch the breaker's rejection too
    .Fallback(Quote.Unavailable)
    .Retry(3)
    .CircuitBreaker(5, TimeSpan.FromSeconds(30));
```

Note the clause: to fall back when the circuit is open, the fallback's handling clause must include `CircuitOpenException`.
