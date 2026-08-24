---
sidebar_position: 19
---

# Coming from Polly?

Kevlar's pipeline model translates 1:1 from Polly v8 — the "first strategy added is outermost" rule is the same, so existing pipelines port mechanically. What changes is the amount of ceremony.

## Translation table

| Polly v8 | Kevlar |
|---|---|
| `new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions { … }).Build()` | `Shield.Retry(3)` |
| `ShouldHandle = new PredicateBuilder().Handle<T>()` | `Shield.When<T>().…` for an ambient rule, or `options.HandlesException` / `HandlesResult` for a direct per-strategy equivalent |
| `ResiliencePipeline` / `ResiliencePipeline<T>` | `Shield` / `Shield<T>` |
| `ResilienceContextPool.Shared.Get(...)` + `Return` | automatic — contexts are pooled internally |
| `BrokenCircuitException` | `CircuitOpenException` (with `RetryAfter`) |
| `TimeoutRejectedException` | `TimeoutExceededException` |
| `CircuitBreakerManualControl` + `StateProvider` | one `CircuitBreakerMonitor` |
| `AddConcurrencyLimiter(10, 20)` | `Shield.ConcurrencyLimit(10, maxQueue: 20)` |
| Delegates must return `ValueTask` | `Task`-returning methods flow straight in: `shield.ExecuteAsync(ct => client.GetAsync(url, ct))` |
| Retry default: constant 2s, no jitter | exponential + jitter, 30s cap |
| First strategy added is outermost | same rule — pipelines translate 1:1 |

:::warning `TimeoutExceededException` is **not** a `System.TimeoutException`

`Kevlar.TimeoutExceededException` derives from `KevlarException`, not from `System.TimeoutException`.
Polly set the same trap — `TimeoutRejectedException` derived from `ExecutionRejectedException`, never
from `System.TimeoutException` — and the reflex is the same in both libraries: a `catch (TimeoutException)` <!-- doc-lint: allow-TimeoutException -->
carried over from application code still compiles, never matches, and lets the timeout escape as an
unhandled exception.

Catch the exception the strategy actually throws, or `KevlarException` for any Kevlar rejection:

```csharp
var saveShield = Shield.Timeout(TimeSpan.FromSeconds(30));

try
{
    await saveShield.ExecuteAsync(ct => SaveAsync(ct), cancellationToken);
}
catch (TimeoutExceededException exception)      // not System.TimeoutException
{
    logger.LogWarning("Timed out after {Timeout}", exception.Timeout);
}
catch (KevlarException exception)               // CircuitOpenException, RateLimitExceededException, …
{
    logger.LogWarning(exception, "Shielded call was rejected");
}
```

The same applies to `CircuitOpenException`, `RateLimitExceededException` and
`ConcurrencyLimitExceededException`: every rejection Kevlar raises derives from `KevlarException`.

:::

## Worked example

Polly v8:

```csharp
var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddTimeout(TimeSpan.FromSeconds(30))
    .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(r => (int)r.StatusCode >= 500),
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
    {
        FailureRatio = 0.5,
        MinimumThroughput = 20,
        BreakDuration = TimeSpan.FromSeconds(30),
    })
    .Build();

var response = await pipeline.ExecuteAsync(
    async token => await client.GetAsync(url, token), cancellationToken);
```

Kevlar:

```csharp
var shield = Shield.For<HttpResponseMessage>()
    .Timeout(TimeSpan.FromSeconds(30))
    .When<HttpRequestException>()
    .OrResult(r => (int)r.StatusCode >= 500)
    .Retry(3)
    .CircuitBreaker(o => { o.FailureRatio = 0.5; o.MinimumThroughput = 20; o.BreakDuration = TimeSpan.FromSeconds(30); });

var response = await shield.ExecuteAsync(ct => client.GetAsync(url, ct), cancellationToken);
```

Note the handling clause: written once, it covers the retry *and* the breaker. In Polly each strategy carries its own `ShouldHandle` predicate.

## Semantic differences worth knowing

- **Retry defaults differ.** Polly's default is constant 2s delays with no jitter; Kevlar's is exponential-with-jitter from 250ms capped at 30s. If you relied on Polly's default timing, say so explicitly: `Shield.Retry(3, Backoff.Constant(TimeSpan.FromSeconds(2)))`.
- **Standard HTTP retry delays are bounded.** `HttpShield.Standard()` honours `Retry-After` but caps every retry delay at 10 seconds by default. Set `StandardHttpShieldOptions.Retry.MaxDelay` to choose another bound.
- **Handling clauses are ambient within one fluent chain.** A clause applies to every reactive strategy after it until replaced; call `WhenAnyError()` to return subsequent strategies to Kevlar's default. `Wrap` and `Compose` seal clauses at the composition boundary: strategies already inside keep their handling, while strategies appended afterwards use the default unless you declare a new local clause. For a Polly strategy with a distinct `ShouldHandle`, set that strategy's `HandlesException` / `HandlesResult` options; a local override fully replaces the ambient clause.
- **Default handling is narrower.** Polly's default predicate handles every exception except
  `OperationCanceledException`. Kevlar additionally lets `CircuitOpenException`,
  `RateLimitExceededException`, `ConcurrencyLimitExceededException`, and fatal runtime exceptions
  propagate. Use an explicit clause when a pipeline intentionally recovers one of those outcomes.
- **One shield, every shape.** There's no separate sync/async pipeline type: `shield.Execute(...)` and `shield.ExecuteAsync(...)` are the same instance. (Hedging is async-only, as in Polly.)
- **Nonsense orders fail fast.** Chaining a `Fallback` *after* a retry, hedge or breaker that shares its handling clause throws at build time, because the fallback would swallow every failure before the outer strategy saw one. Polly builds such pipelines silently. Put the fallback first (outermost), or give it a narrower clause.
- **State sharing is by instance.** Like Polly, strategy state (circuits, buckets) lives with the built shield object — reuse the instance to share it. `Wrap`/`Compose` reference, not copy. See [Composition](composition.md#the-state-sharing-rule).
