---
sidebar_position: 5
---

# Composition

## Chain order = execution order

**The first strategy in a chain is the outermost** — the same rule as ASP.NET middleware:

```csharp
Shield
    .Timeout(TimeSpan.FromSeconds(30))   // 1. total budget around everything below
    .Retry(3)                            // 2. retries happen inside that budget
    .CircuitBreaker(5, TimeSpan.FromSeconds(30))  // 3. breaker sees each attempt
    .Timeout(TimeSpan.FromSeconds(5));   // 4. each individual attempt gets 5s
```

Reading top to bottom tells you exactly what happens: the outer timeout caps the whole thing, retries happen inside it, each retried attempt goes through the breaker, and each attempt individually gets 5 seconds.

This is the classic "total timeout outside, attempt timeout inside" pattern — one chain, no nesting.

## Retry, hedge, and timeout scopes

Order also defines how retry and hedging multiply work:

- `Retry(r).Hedge(h)` creates at most `r + 1` hedge groups, each containing at most `h`
  attempts. A new group starts only after the previous group is exhausted.
- `Hedge(h).Retry(r)` creates at most `h` hedge attempts, each with its own retry loop of at
  most `r + 1` invocations.
- The maximum in either order is `(r + 1) × h`, but a winner, unhandled exception, or caller
  cancellation stops the outer strategy from multiplying more work.

Timeout position follows the same rule. `Timeout(t).Hedge(h)` is one total budget around every
fork. `Hedge(h).Timeout(t)` gives each fork an independent budget. Cancelling hedge losers does
not count as their timeout and does not invoke their `OnTimeout` callback.

## Merging independent shields

Use `Wrap` to put one shield around another, or `Compose` to stack several:

```csharp
var breaker  = Shield.CircuitBreaker(5, TimeSpan.FromSeconds(30));   // built once — holds the circuit state
var reads    = Shield.Retry(3).Wrap(breaker);
var writes   = Shield.Timeout(TimeSpan.FromSeconds(5)).Wrap(breaker);
// reads and writes share ONE circuit: failures through either trip both.

var combined = Shield.Compose(timeoutShield, retryShield, breakerShield);  // first = outermost
```

:::note What survives a merge
`Wrap` and `Compose` carry the *strategies* (with their state) forward. `Compose` also keeps the first non-null name and `TimeProvider` among its inputs, and the last input's ambient [handling clause](handling-failures.md) stays ambient for further chaining. `outer.Wrap(inner)` keeps the outer shield's clause, name and time provider.
:::

## The state-sharing rule

> **Strategy state lives with the shield instance that created it.**

That's the whole rule. Consequences:

- Reuse a shield instance across call sites → those call sites share the circuit breaker's state, the rate limiter's token bucket, the concurrency limit's slots.
- Build a new shield (even with identical configuration) → fresh, independent state.
- `Wrap` and `Compose` don't copy state — they reference the wrapped shield, so the sharing above works across merged shields too.

This is deliberate. A circuit breaker that doesn't share state across the call sites hitting the same dependency isn't protecting anything; a rate limiter with per-call-site buckets isn't limiting anything.

```csharp
// One breaker guarding one downstream dependency, shared by two shaped pipelines:
var downstreamBreaker = Shield.CircuitBreaker(o => { o.FailureRatio = 0.5; o.MinimumThroughput = 20; });

var interactive = Shield.Timeout(TimeSpan.FromSeconds(2)).Wrap(downstreamBreaker);
var background  = Shield.Timeout(TimeSpan.FromSeconds(30)).Retry(5).Wrap(downstreamBreaker);
```

## Handling clauses flow down the chain

A [handling clause](handling-failures.md) applies to the strategy it precedes *and* to every reactive strategy chained after it, until you write a new clause:

```csharp
Shield
    .When<HttpRequestException>()
    .Retry(3)                      // retries HttpRequestException
    .CircuitBreaker(5, breakDur)   // breaker also counts HttpRequestException
    .When<TimeoutExceededException>()
    .Fallback(...);                // fallback reacts to TimeoutExceededException only
```

## Impossible orders fail fast

One ordering is always a bug: a `Fallback` chained *after* (inside) a retry, hedge or circuit breaker that shares its handling clause. The fallback recovers every failure before the outer strategy sees one, silently disabling it. Kevlar refuses to build that chain:

```csharp
Shield.For<int>().Retry(3).Fallback(-1);
// InvalidOperationException: … makes Retry(3, …) unreachable.
// Chain the Fallback first (the first strategy is the outermost) …

Shield.For<int>().Fallback(-1).Retry(3);   // ✔ retry runs inside, fallback recovers after it gives up
```

A fallback with its own *narrower* clause is still allowed inside — that's a deliberate layered recovery, not a mistake.

## See what you built

Every shield describes itself — `ToString()` prints the pipeline, outermost first, with each strategy's configuration:

```csharp
var shield = Shield
    .Timeout(TimeSpan.FromSeconds(30))
    .Retry(3)
    .CircuitBreaker(5, TimeSpan.FromSeconds(30))
    .WithName("github");

logger.LogInformation("using {Shield}", shield);
// github: Timeout(30s) → Retry(3, exponential 250ms ×2 +jitter ≤30s) → CircuitBreaker(5 consecutive, break 30s)
```

Log it at startup and code review becomes "read the log line".
