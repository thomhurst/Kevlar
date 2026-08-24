---
sidebar_position: 20
---

# Exceptions

Kevlar preserves application exceptions and their original stack traces. A strategy introduces a
new exception only when it rejects an execution, cannot safely continue it, injects a configured
fault, or reports a testing assertion failure.

## Reference

The **default clause** column says whether a reactive strategy handles the exception when no custom
[`When` clause](handling-failures.md) is present. The default handles ordinary errors but excludes
`OperationCanceledException`, circuit-open, rate-limit, and concurrency-limit rejections, and fatal
runtime exceptions. Use an explicit clause to include an excluded rejection or narrow the ordinary
errors handled by a retry, breaker, hedge, or fallback.

| Exception | Thrown by | Properties | Base class | Catch pattern | Default clause |
|---|---|---|---|---|---|
| `KevlarException` | Abstract base for core strategy rejections | Inherited `InnerException` carries a cause when the concrete rejection has one. | `Exception` | `catch (KevlarException)` | N/A |
| `KevlarProxyException` | Internal bookkeeping for adapter-owned exception proxies | `OriginalException` is the failure exposed to handling clauses, public outcomes, and adapter callers; inherited `InnerException` preserves the same cause. | `Exception` | Catch `OriginalException`'s concrete type, such as `RpcException`. | N/A |
| `TimeoutExceededException` | Timeout | `Timeout` is the winning budget; a strategy-produced timeout carries the delegate's cancellation exception in inherited `InnerException`. The public one-argument constructor leaves it `null`. | `KevlarException` | `catch (TimeoutExceededException e) when (e.Timeout == budget)` | Yes |
| `CircuitOpenException` | Circuit breaker | `RetryAfter` is the remaining break duration; `IsIsolated` distinguishes manual isolation; inherited `InnerException` is the last failure when known. | `KevlarException` | `catch (CircuitOpenException e) when (e.RetryAfter is { } delay)` | No |
| `RateLimitExceededException` | Rate limit | `RetryAfter` estimates when a permit may become available. | `KevlarException` | `catch (RateLimitExceededException e) when (e.RetryAfter is { } delay)` | No |
| `ConcurrencyLimitExceededException` | Concurrency limit | Inherited `InnerException` is `null`; the rejection means both execution and queue capacity are full. | `KevlarException` | `catch (ConcurrencyLimitExceededException)` | No |
| `HttpRequestReplayException` | HTTP request replay and endpoint routing | Inherited `InnerException` is the content failure when serialization or buffering caused the replay failure. | `InvalidOperationException` | `catch (HttpRequestReplayException e)` | Yes |
| `ChaosInjectedException` | Chaos fault injection | Inherited `InnerException` is populated only when the configured injected fault wraps a cause. | `Exception` | `catch (ChaosInjectedException e)` | Yes |
| `ShieldAssertionException` | Kevlar.Testing assertions and bounded waits | Inherited `InnerException` is `null`; `Message` explains the failed assertion or unmet condition. | `Exception` | `catch (ShieldAssertionException e)` | Yes |

`RetryAfter` can be `null`: callers must treat it as advisory. A manually isolated circuit has
`IsIsolated == true` and no automatic retry time. `CircuitOpenException.InnerException` is diagnostic
history, not a new failure from the rejected call.

## Catching core rejections

Catch a concrete type when its metadata changes recovery behavior. Catch `KevlarException` only when
all core rejections share one response.

<!-- doc-test-run: catch-kevlar-exception -->
```csharp
var caughtKevlarException = false;
try
{
    await Shield.Empty.ExecuteAsync<int>(
        _ => throw new TimeoutExceededException(TimeSpan.FromSeconds(1)));
}
catch (KevlarException)
{
    caughtKevlarException = true;
}

if (!caughtKevlarException)
{
    throw new InvalidOperationException("The KevlarException catch did not match.");
}
```

<!-- doc-test-run: catch-timeout-exceeded -->
```csharp
var budget = TimeSpan.FromSeconds(1);
var caughtTimeout = false;
try
{
    await Shield.Empty.ExecuteAsync<int>(_ => throw new TimeoutExceededException(budget));
}
catch (TimeoutExceededException exception) when (exception.Timeout == budget)
{
    caughtTimeout = true;
}

if (!caughtTimeout)
{
    throw new InvalidOperationException("The timeout catch did not match.");
}
```

<!-- doc-test-run: catch-circuit-open -->
```csharp
var caughtOpenCircuit = false;
try
{
    await Shield.Empty.ExecuteAsync<int>(_ => throw new CircuitOpenException(
        TimeSpan.FromSeconds(5), isIsolated: false, lastException: null));
}
catch (CircuitOpenException exception) when (exception.RetryAfter is { } retryAfter
    && retryAfter > TimeSpan.Zero)
{
    caughtOpenCircuit = true;
}

if (!caughtOpenCircuit)
{
    throw new InvalidOperationException("The circuit-open catch did not match.");
}
```

<!-- doc-test-run: catch-rate-limit -->
```csharp
var caughtRateLimit = false;
try
{
    await Shield.Empty.ExecuteAsync<int>(
        _ => throw new RateLimitExceededException(TimeSpan.FromSeconds(1)));
}
catch (RateLimitExceededException exception) when (exception.RetryAfter is { } retryAfter
    && retryAfter > TimeSpan.Zero)
{
    caughtRateLimit = true;
}

if (!caughtRateLimit)
{
    throw new InvalidOperationException("The rate-limit catch did not match.");
}
```

<!-- doc-test-run: catch-concurrency-limit -->
```csharp
var caughtConcurrencyLimit = false;
try
{
    await Shield.Empty.ExecuteAsync<int>(_ => throw new ConcurrencyLimitExceededException());
}
catch (ConcurrencyLimitExceededException)
{
    caughtConcurrencyLimit = true;
}

if (!caughtConcurrencyLimit)
{
    throw new InvalidOperationException("The concurrency-limit catch did not match.");
}
```

## Satellite exceptions

Satellite packages use their own exception types because these failures are not core strategy
rejections.

<!-- doc-test-run: catch-http-replay -->
```csharp
var caughtReplay = false;
try
{
    await Shield.Empty.ExecuteAsync<int>(_ => throw new HttpRequestReplayException(
        "The request body cannot be replayed."));
}
catch (HttpRequestReplayException exception) when (exception.InnerException is null)
{
    caughtReplay = true;
}

if (!caughtReplay)
{
    throw new InvalidOperationException("The HTTP replay catch did not match.");
}
```

<!-- doc-test-run: catch-chaos-injected -->
```csharp
var caughtChaos = false;
try
{
    await Shield.Empty.ExecuteAsync<int>(_ => throw new ChaosInjectedException());
}
catch (ChaosInjectedException)
{
    caughtChaos = true;
}

if (!caughtChaos)
{
    throw new InvalidOperationException("The chaos catch did not match.");
}
```

<!-- doc-test-run: catch-shield-assertion -->
```csharp
var caughtAssertion = false;
try
{
    await Shield.Empty.ExecuteAsync<int>(_ => throw new ShieldAssertionException(
        "The expected strategy was absent."));
}
catch (ShieldAssertionException exception) when (exception.Message.Contains("strategy"))
{
    caughtAssertion = true;
}

if (!caughtAssertion)
{
    throw new InvalidOperationException("The testing assertion catch did not match.");
}
```

## Timeout is not `System.TimeoutException`

`TimeoutExceededException` deliberately derives from `KevlarException`, not
`System.TimeoutException`. A `catch (TimeoutException)` compiles but does not match a Kevlar timeout. <!-- doc-lint: allow-TimeoutException -->
This mirrors Polly's `TimeoutRejectedException` behavior. Catch `TimeoutExceededException` and inspect
its `Timeout` property.

## Stack traces and inner exceptions

An application exception remains the same exception instance while it travels through a pipeline.
At the execution boundary Kevlar rethrows it with `ExceptionDispatchInfo`, preserving the original
throw site. `ExecuteOutcomeAsync` returns it instead through `Outcome<T>.Exception`.

A rejection has its own stack trace because the strategy creates it. Only documented causal data is
placed in `InnerException`: the delegate's cancellation when a timeout fires, the last breaker
failure, a wrapped chaos cause, or an HTTP replay content failure. Do not assume every Kevlar-created
exception has an inner exception.

## Configuration failures

There is currently no public `KevlarConfigurationException`. Direct shorthand arguments report
`ArgumentException` or `ArgumentOutOfRangeException`; invalid values supplied through an options
callback currently use the same framework exception family. Catch configuration failures during
startup, not around every execution.
