---
sidebar_position: 3
---

# Handling Failures

Reactive strategies — [retry](strategies/retry.md), [circuit breaker](strategies/circuit-breaker.md), [hedging](strategies/hedging.md), [fallback](strategies/fallback.md) — act on failures. Handling clauses tell them what a failure *is*.

## The default

With no handling clause, the default is: **any exception except `OperationCanceledException`**. Cancellation isn't a fault; retrying it would fight the caller.

When you execute with `ExecuteOutcomeAsync`, use `outcome.TryGetResult(out var result)` to
consume a successful result without throwing. If it returns `false`, the final captured failure
remains available through `outcome.Exception`.

## Exception clauses

```csharp
var shield = Shield
    .When<HttpRequestException>()                     // this exception type (and subtypes)
    .Or<TimeoutExceededException>()                     // or this one
    .OrWhen(ex => ex is IOException { Message: var m } && m.Contains("pipe"))  // or any predicate
    .Retry(5);
```

- `When<TException>()` starts a clause matching `TException` and anything derived from it. `When<TException>(predicate)` narrows it further, and `When(predicate)` matches on any exception.
- `Or<TException>()` / `Or<TException>(predicate)` / `OrWhen(predicate)` add alternatives to the clause. All alternatives OR together.

Both builders speak the same grammar. `When…` and `Or…` are interchangeable — the convention is `When` for the first clause and `Or` for each addition: `When<A>().Or<B>().OrWhen(...)`.

## Result clauses

Sometimes failure isn't an exception — it's a well-formed response you don't like (an HTTP 500, an empty payload, a `Status = "Retry"` field). Lift into a typed shield with `For<T>` and add `WhenResult`:

```csharp
var http = Shield.For<HttpResponseMessage>()
    .When<HttpRequestException>()
    .WhenResult(r => (int)r.StatusCode >= 500)
    .Retry(3);
```

Now a 503 response triggers a retry exactly as a thrown `HttpRequestException` would. The delegate's return value is inspected — nothing is thrown internally, the outcome just counts as a failure.

Typed builders add result alternatives with `OrResult(predicate)` / `OrResult(value)`, and two shorthands for the most common check of all:

```csharp
Shield.For<User?>().WhenDefault().Retry(2);   // retry when the result is null / default
// mid-chain: .OrDefault() adds the same check to an existing clause
```

## Clauses are ambient

A clause applies to the strategy it creates *and* to every reactive strategy chained after it, until you write a new clause:

<!-- doc-test-ignore: Uses an ellipsis for the application-specific fallback implementation. -->
```csharp
Shield
    .When<HttpRequestException>()      // clause #1
    .Retry(3)                            //   ← uses clause #1
    .CircuitBreaker(5, breakDuration)    //   ← also clause #1
    .When<TimeoutExceededException>()  // clause #2 replaces #1 from here on
    .Fallback(...);                      //   ← uses clause #2
```

This is why most chains only need one clause, written once at the top — and why you never repeat a `ShouldHandle` predicate per strategy like in Polly v8.

:::info Proactive strategies don't consult clauses
Timeouts, rate limits and concurrency limits don't care why something failed — they act on time and concurrency, not outcomes. Clauses only drive the reactive strategies.
:::

:::info Clauses survive lifting and composing
`shield.For<T>()` carries an ambient exception clause into the typed shield, and `Shield.Compose(...)` keeps the last input's clause ambient — so strategies chained afterwards keep handling what you declared. Write a new clause any time you want different handling.
:::
