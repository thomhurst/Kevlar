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
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30))  // 3. breaker sees each attempt
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
var breaker  = Shield.CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));   // built once — holds the circuit state
var reads    = Shield.Retry(3).Wrap(breaker);
var writes   = Shield.Timeout(TimeSpan.FromSeconds(5)).Wrap(breaker);
// reads and writes share ONE circuit: failures through either trip both.

var combined = Shield.Compose(timeoutShield, retryShield, breakerShield);  // first = outermost
```

The two are the same operation, so there is no semantic difference to hunt for: `outer.Wrap(inner)`
and `Shield.Compose(outer, inner)` produce the same strategy order, keep the same first non-null
name and `TimeProvider`, and seal the ambient clause identically. Write `Wrap` when one shield
reads as the scope around another, and `Compose` when several stack as peers.

Result-aware shields compose the same way through `Shield<T>.Compose`, with the same metadata and
clause-sealing rules:

```csharp
var typedTimeout = Shield.For<HttpResponseMessage>().Timeout(TimeSpan.FromSeconds(5));
var typedRetry = Shield.For<HttpResponseMessage>().Retry(3);

var typed = Shield<HttpResponseMessage>.Compose(typedTimeout, typedRetry);  // first = outermost
```

:::note What survives a merge
`Wrap` and `Compose` carry the *strategies* (with their state) forward. Both keep the first non-null name and `TimeProvider` from outer to inner. Ambient [handling clauses](handling-failures.md) stop at the composition boundary, so reactive strategies appended to the merged shield use default handling unless you declare a new clause.

Strategies already inside each input keep the handling rules they were built with. Composition seals only the ambient clause that would otherwise affect later additions; it does not change existing pipeline behavior. In `outer.Wrap(inner)`, outer metadata still wins when present.
:::

## The state-sharing rule

> **Strategy state lives with the shield instance that created it.**

That's the whole rule. Consequences:

- Reuse a shield instance across call sites → those call sites share the circuit breaker's state, the rate limiter's token bucket, the concurrency limit's slots.
- Build a new shield (even with identical configuration) → fresh, independent state.
- `Wrap` and `Compose` don't copy state — they reference the wrapped shield, so the sharing above works across merged shields too.
- Stateless custom strategy instances may appear more than once in one chain. Kevlar rejects duplicate references to built-in stateful breakers and limiters because nesting the same instance can deadlock or double-count. A stateful custom `Strategy` can opt into the same protection by overriding `IsDuplicateReferenceUnsafe` and returning `true`.

This is deliberate. A circuit breaker that doesn't share state across the call sites hitting the same dependency isn't protecting anything; a rate limiter with per-call-site buckets isn't limiting anything.

```csharp
// One breaker guarding one downstream dependency, shared by two shaped pipelines:
var downstreamBreaker = Shield.CircuitBreaker(o => { o.FailureRatio = 0.5; o.MinimumThroughput = 20; });

var interactive = Shield.Timeout(TimeSpan.FromSeconds(2)).Wrap(downstreamBreaker);
var background  = Shield.Timeout(TimeSpan.FromSeconds(30)).Retry(5).Wrap(downstreamBreaker);
```

## Handling clauses flow down a fluent chain

:::tip The ambient clause rule
A [handling clause](handling-failures.md) applies to **the strategy it is attached to and to every reactive strategy chained after it**, until a new clause replaces it, `WhenAnyError()` resets it, or `Wrap`/`Compose` seals it at a composition boundary.
:::

The circuit breaker below never declares a clause of its own. It inherits the one written at the top of the chain, so only `HttpRequestException` counts toward tripping it:

```csharp
var shield = Shield
    .When<HttpRequestException>()
    .Retry(3)                                                                     // retries HttpRequestException
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
    // ↑ the breaker counts HttpRequestException failures only
```

Proactive strategies — timeout, rate limit, concurrency limit — never consult a clause, but they do not end it either: the clause stays ambient for the next reactive strategy. A clause that reaches no reactive strategy at all is dead, and the optional analyzer reports it as [`KEV007`](analyzers.md#kev007-dead-handling-clause).

Writing a new clause mid-chain replaces the previous one from that point on:

<!-- doc-test-ignore: Uses an ellipsis to illustrate a fallback body defined by the application. -->
```csharp
Shield
    .When<HttpRequestException>()
    .Retry(3)                      // retries HttpRequestException
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: breakDur)   // breaker also counts HttpRequestException
    .When<TimeoutExceededException>()
    .Fallback(...);                // fallback reacts to TimeoutExceededException only
```

`Wrap` and `Compose` are scope boundaries. A reactive strategy added afterwards uses Kevlar's default handling—any exception except `OperationCanceledException`—unless the new expression declares a clause locally:

```csharp
var apiDefaults = Shield.When<HttpRequestException>().Retry(3);

var shield = Shield.Timeout(TimeSpan.FromSeconds(30))
    .Wrap(apiDefaults)
    .When<HttpRequestException>()
    .CircuitBreaker(
        consecutiveFailures: 5,
        breakDuration: TimeSpan.FromSeconds(30));
```

## Impossible orders fail fast

One ordering is always a bug: a `Fallback` chained *after* (inside) a retry, hedge or circuit breaker that shares its handling clause. The fallback recovers every failure before the outer strategy sees one, silently disabling it. Kevlar refuses to build that chain:

<!-- doc-test-run: invalid-composition -->
```csharp
Shield.For<int>().Retry(3).FallbackTo(-1);
// InvalidOperationException: … makes Retry(3, …) unreachable.
// Chain the Fallback first (the first strategy is the outermost) …

Shield.For<int>().FallbackTo(-1).Retry(3);   // ✔ retry runs inside, fallback recovers after it gives up
```

A fallback with its own *narrower* clause is still allowed inside — that's a deliberate layered recovery, not a mistake.

## See what you built

Every shield describes itself — `ToString()` prints the pipeline, outermost first, with each strategy's configuration:

```csharp
var shield = Shield
    .Timeout(TimeSpan.FromSeconds(30))
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30))
    .WithName("github");

logger.LogInformation("using {Shield}", shield);
// github: Timeout(30s) → Retry(3, exponential 250ms ×2 +jitter ≤30s) → CircuitBreaker(5 consecutive, break 30s)
```

Log it at startup and code review becomes "read the log line".
