---
sidebar_position: 3
---

# Handling Failures

Reactive strategies — [retry](strategies/retry.md), [circuit breaker](strategies/circuit-breaker.md), [hedging](strategies/hedging.md), [fallback](strategies/fallback.md) — act on failures. Handling clauses tell them what a failure *is*.

## The default

With no handling clause, the default is: **any exception except `OperationCanceledException`**. Cancellation isn't a fault; retrying it would fight the caller.

## Exception clauses

```csharp
var policy = Policy
    .Handle<HttpRequestException>()                     // this exception type (and subtypes)
    .Or<TimeoutExceededException>()                     // or this one
    .OrWhen(ex => ex is IOException { Message: var m } && m.Contains("pipe"))  // or any predicate
    .Retry(5);
```

- `Handle<TException>()` starts a clause matching `TException` and anything derived from it. `Handle<TException>(predicate)` narrows it further, and `HandleWhen(predicate)` matches on any exception.
- `Or<TException>()` / `Or<TException>(predicate)` / `OrWhen(predicate)` add alternatives to the clause. All alternatives OR together.

On **typed** builders (`Policy.For<T>()...`) the spelling differs slightly: `Handle` is repeatable instead of switching to `Or` — `Handle<A>().Handle<B>().HandleResult(...)` accumulates the same way.

## Result clauses

Sometimes failure isn't an exception — it's a well-formed response you don't like (an HTTP 500, an empty payload, a `Status = "Retry"` field). Lift into a typed policy with `For<T>` and add `HandleResult`:

```csharp
var http = Policy.For<HttpResponseMessage>()
    .Handle<HttpRequestException>()
    .HandleResult(r => (int)r.StatusCode >= 500)
    .Retry(3);
```

Now a 503 response triggers a retry exactly as a thrown `HttpRequestException` would. The delegate's return value is inspected — nothing is thrown internally, the outcome just counts as a failure.

## Clauses are ambient

A clause applies to the strategy it creates *and* to every reactive strategy chained after it, until you write a new clause:

```csharp
Policy
    .Handle<HttpRequestException>()      // clause #1
    .Retry(3)                            //   ← uses clause #1
    .CircuitBreaker(5, breakDuration)    //   ← also clause #1
    .Handle<TimeoutExceededException>()  // clause #2 replaces #1 from here on
    .Fallback(...);                      //   ← uses clause #2
```

This is why most chains only need one clause, written once at the top — and why you never repeat a `ShouldHandle` predicate per strategy like in Polly v8.

:::info Proactive strategies don't consult clauses
Timeouts, rate limits and bulkheads don't care why something failed — they act on time and concurrency, not outcomes. Clauses only drive the reactive strategies.
:::

:::warning Two things reset the clause
`policy.For<T>()` (lifting an existing untyped policy to a typed one) and `Policy.Compose(...)` both discard the ambient clause — re-declare `Handle`/`HandleResult` afterwards if you chain more reactive strategies.
:::
