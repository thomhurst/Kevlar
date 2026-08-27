---
sidebar_position: 20
---

# Exceptions

Kevlar preserves application exceptions and their original stack traces. A strategy introduces a
new exception only when it rejects an execution, cannot safely continue it, injects a configured
fault, or reports a testing assertion failure.

## Reference

The **default clause** column says whether a reactive strategy handles the exception when no custom
[`When` clause](handling-failures.md) is present. Retry, circuit breaker, and hedge handle ordinary
errors but exclude `OperationCanceledException`, fail-fast execution rejections, and fatal runtime
exceptions. Fallback additionally handles `ExecutionRejectedException` so it can recover an outer
breaker or limiter without opting retry into the same rejection. A timeout remains handled because
the delegate ran and produced a recoverable attempt failure. An explicit ambient clause or local
handling override replaces the strategy default.

| Exception | Thrown by | Properties | Base class | Catch pattern | Default clause |
|---|---|---|---|---|---|
| `KevlarException` | Abstract base for exceptions raised by core strategies | Inherited `InnerException` carries a cause when the concrete exception has one. | `Exception` | `catch (KevlarException)` | N/A |
| `KevlarConfigurationException` | Invalid values supplied through a strategy options callback or returned by a dynamic duration generator | `Message` names the options type, property, requirement, and offending value. | `KevlarException` | Catch during startup while building shields. | N/A |
| `ExecutionRejectedException` | Abstract base for execution rejections | `RetryAfter` estimates when execution may be attempted again; inherited `InnerException` carries a cause when known. | `KevlarException` | `catch (ExecutionRejectedException e)` | N/A |
| `KevlarProxyException` | Internal bookkeeping for adapter-owned exception proxies | `OriginalException` is the failure exposed to handling clauses, public outcomes, and adapter callers; inherited `InnerException` preserves the same cause. | `Exception` | Catch `OriginalException`'s concrete type, such as `RpcException`. | N/A |
| `TimeoutExceededException` | Timeout | `Timeout` is the winning budget; a strategy-produced timeout carries the delegate's cancellation exception in inherited `InnerException`. | `KevlarException` | `catch (TimeoutExceededException e) when (e.Timeout == budget)` | Yes |
| `CircuitOpenException` | Circuit breaker | Inherited `RetryAfter` is the remaining break duration; `IsIsolated` distinguishes manual isolation; inherited `InnerException` is the last failure when known. | `ExecutionRejectedException` | `catch (CircuitOpenException e) when (e.RetryAfter is { } delay)` | Fallback only |
| `RateLimitExceededException` | Rate limit | Inherited `RetryAfter` estimates when a permit may become available. | `ExecutionRejectedException` | `catch (RateLimitExceededException e) when (e.RetryAfter is { } delay)` | Fallback only |
| `RateLimiterAdapterRejectedException` | System.Threading.RateLimiting adapter | Inherited `RetryAfter` is copied from rejected lease metadata when supplied. | `ExecutionRejectedException` | `catch (RateLimiterAdapterRejectedException e) when (e.RetryAfter is { } delay)` | Fallback only |
| `ConcurrencyLimitExceededException` | Concurrency limit | Inherited `RetryAfter` and `InnerException` are `null`; the rejection means both execution and queue capacity are full. | `ExecutionRejectedException` | `catch (ConcurrencyLimitExceededException)` | Fallback only |
| `HttpRequestReplayException` | HTTP request replay and endpoint routing | Inherited `InnerException` is the content failure when serialization or buffering caused the replay failure. | `InvalidOperationException` | `catch (HttpRequestReplayException e)` | Yes |
| `ChaosInjectedException` | Chaos fault injection | Inherited `InnerException` is populated only when the configured injected fault wraps a cause. | `Exception` | `catch (ChaosInjectedException e)` | Yes |
| `ShieldAssertionException` | Kevlar.Testing assertions and bounded waits | Inherited `InnerException` is `null`; `Message` explains the failed assertion or unmet condition. | `Exception` | `catch (ShieldAssertionException e)` | Yes |

Every concrete exception exposes `()`, `(string)`, and `(string, Exception)` constructors. Metadata-
specific constructors remain the preferred way to represent a real strategy rejection.

`ExecutionRejectedException.RetryAfter` can be `null`: callers must treat it as advisory. A manually
isolated circuit has `IsIsolated == true` and no automatic retry time.
`CircuitOpenException.InnerException` is diagnostic history, not a new failure from the rejected
call.

An execution rejection is fail-fast: the protected delegate did not run. A timeout is different:
the delegate ran, exceeded its budget, and was abandoned after cancellation. For that reason,
`TimeoutExceededException` derives directly from `KevlarException`, outside the rejection family.

## Catching core rejections

Catch a concrete type when its metadata changes recovery behavior. Catch
`ExecutionRejectedException` when every fail-fast rejection shares one response; this catch does
not match timeouts. Catch `KevlarException` only when timeouts, rejections, configuration failures,
and future core exception families should share handling.

<!-- doc-test-run: execution-rejection-does-not-catch-timeout -->
```csharp
var caughtTimeoutOutsideRejectionFamily = false;
try
{
    await Shield.Empty.ExecuteAsync<int>(
        _ => throw new TimeoutExceededException(TimeSpan.FromSeconds(1)));
}
catch (ExecutionRejectedException)
{
    throw new InvalidOperationException("A timeout must not be caught as a fail-fast rejection.");
}
catch (TimeoutExceededException)
{
    caughtTimeoutOutsideRejectionFamily = true;
}

if (!caughtTimeoutOutsideRejectionFamily)
{
    throw new InvalidOperationException("The timeout catch did not match.");
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
using Kevlar.Extensions.Http;

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
using Kevlar.Chaos;

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
using Kevlar.Testing;

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

`TimeoutExceededException` derives directly from `KevlarException`, not
`ExecutionRejectedException` or `System.TimeoutException`. <!-- doc-lint: allow-TimeoutException --> A `catch (ExecutionRejectedException)`
therefore handles only fail-fast calls where the delegate did not run. A `catch (TimeoutException)` <!-- doc-lint: allow-TimeoutException -->
compiles but does not match a Kevlar timeout. Catch
`TimeoutExceededException` and inspect its `Timeout` property.

## Stack traces and inner exceptions

An application exception remains the same exception instance while it travels through a pipeline.
At the execution boundary Kevlar rethrows it with `ExceptionDispatchInfo`, preserving the original
throw site. `ExecuteOutcomeAsync` returns it instead through `Outcome<T>.Exception`.

A strategy-created exception has its own stack trace. Only documented causal data is placed in
`InnerException`: the delegate's cancellation when a timeout fires, the last breaker failure, a
wrapped chaos cause, or an HTTP replay content failure. Do not assume every Kevlar-created exception
has an inner exception.

## Configuration failures

Invalid values supplied through an options callback throw `KevlarConfigurationException`. Its
message names the options type, property, requirement, and offending value—for example,
`RetryOptions.MaxRetries must not be negative (was -1)`. Invalid values returned later by
`TimeoutGenerator` or `BreakDurationGenerator` use the same exception and name the generator
property.

Direct shorthand arguments keep the framework exception family and the public parameter name:
`Shield.Retry(-1)` throws `ArgumentOutOfRangeException` with `ParamName == "maxRetries"`. Catch
configuration failures while building shields at startup; a generator failure can surface during
execution because its value is produced per call.

## Synchronous execution guards

Synchronous `Execute`, `ExecuteOutcome`, and `ExecuteWithContext` throw `NotSupportedException` for
work that cannot run without leaving the calling thread. Multi-attempt hedging, a
`ValueTask`-returning fallback recovery delegate, and `UseRateLimiter` adapters are rejected before
the action runs. Strategy hooks are guarded at the call instead: every hook returns `ValueTask`, and
one that returns an incomplete `ValueTask` fails that execution with `NotSupportedException` naming
the options type and hook, for example `RetryOptions.OnRetry` or `TimeoutOptions.TimeoutGenerator`.
Hooks that complete synchronously are never rejected. `ExecuteOutcome` returns the guard exception
as a failed outcome. See
[synchronous execution compatibility](executing.md#synchronous-execution-compatibility).
