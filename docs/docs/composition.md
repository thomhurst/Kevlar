---
sidebar_position: 5
---

# Composition

## Chain order = execution order

**The first strategy in a chain is the outermost** — the same rule as ASP.NET middleware:

```csharp
Policy
    .Timeout(TimeSpan.FromSeconds(30))   // 1. total budget around everything below
    .Retry(3)                            // 2. retries happen inside that budget
    .CircuitBreaker(5, TimeSpan.FromSeconds(30))  // 3. breaker sees each attempt
    .Timeout(TimeSpan.FromSeconds(5));   // 4. each individual attempt gets 5s
```

Reading top to bottom tells you exactly what happens: the outer timeout caps the whole thing, retries happen inside it, each retried attempt goes through the breaker, and each attempt individually gets 5 seconds.

This is the classic "total timeout outside, attempt timeout inside" pattern — one chain, no nesting.

## Merging independent policies

Use `Wrap` to put one policy around another, or `Compose` to stack several:

```csharp
var breaker  = Policy.CircuitBreaker(5, TimeSpan.FromSeconds(30));   // built once — holds the circuit state
var reads    = Policy.Retry(3).Wrap(breaker);
var writes   = Policy.Timeout(TimeSpan.FromSeconds(5)).Wrap(breaker);
// reads and writes share ONE circuit: failures through either trip both.

var combined = Policy.Compose(timeoutPolicy, retryPolicy, breakerPolicy);  // first = outermost
```

:::note What survives a merge
`Wrap` and `Compose` carry the *strategies* (with their state) forward. `Compose` does **not** carry over the inputs' ambient [handling clauses](handling-failures.md), names or `TimeProvider` — set those on the composed policy if you need them. `outer.Wrap(inner)` keeps the outer policy's clause, name and time provider.
:::

## The state-sharing rule

> **Strategy state lives with the policy instance that created it.**

That's the whole rule. Consequences:

- Reuse a policy instance across call sites → those call sites share the circuit breaker's state, the rate limiter's token bucket, the bulkhead's slots.
- Build a new policy (even with identical configuration) → fresh, independent state.
- `Wrap` and `Compose` don't copy state — they reference the wrapped policy, so the sharing above works across merged policies too.

This is deliberate. A circuit breaker that doesn't share state across the call sites hitting the same dependency isn't protecting anything; a rate limiter with per-call-site buckets isn't limiting anything.

```csharp
// One breaker guarding one downstream dependency, shared by two shaped pipelines:
var downstreamBreaker = Policy.CircuitBreaker(o => { o.FailureRatio = 0.5; o.MinimumThroughput = 20; });

var interactive = Policy.Timeout(TimeSpan.FromSeconds(2)).Wrap(downstreamBreaker);
var background  = Policy.Timeout(TimeSpan.FromSeconds(30)).Retry(5).Wrap(downstreamBreaker);
```

## Handling clauses flow down the chain

A [handling clause](handling-failures.md) applies to the strategy it precedes *and* to every reactive strategy chained after it, until you write a new clause:

```csharp
Policy
    .Handle<HttpRequestException>()
    .Retry(3)                      // retries HttpRequestException
    .CircuitBreaker(5, breakDur)   // breaker also counts HttpRequestException
    .Handle<TimeoutExceededException>()
    .Fallback(...);                // fallback reacts to TimeoutExceededException only
```
