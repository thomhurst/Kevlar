---
sidebar_position: 12
---

# Coming from Polly?

Kevlar's pipeline model translates 1:1 from Polly v8 — the "first strategy added is outermost" rule is the same, so existing pipelines port mechanically. What changes is the amount of ceremony.

## Translation table

| Polly v8 | Kevlar |
|---|---|
| `new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions { … }).Build()` | `Policy.Retry(3)` |
| `ShouldHandle = new PredicateBuilder().Handle<T>()` | `Policy.Handle<T>().…` (ambient for the whole chain) |
| `ResiliencePipeline` / `ResiliencePipeline<T>` | `Policy` / `Policy<T>` |
| `ResilienceContextPool.Shared.Get(...)` + `Return` | automatic — contexts are pooled internally |
| `BrokenCircuitException` | `CircuitOpenException` (with `RetryAfter`) |
| `TimeoutRejectedException` | `TimeoutExceededException` |
| `CircuitBreakerManualControl` + `StateProvider` | one `CircuitBreakerMonitor` |
| Retry default: constant 2s, no jitter | exponential + jitter, 30s cap |
| First strategy added is outermost | same rule — pipelines translate 1:1 |

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
var policy = Policy.For<HttpResponseMessage>()
    .Timeout(TimeSpan.FromSeconds(30))
    .Handle<HttpRequestException>()
    .HandleResult(r => (int)r.StatusCode >= 500)
    .Retry(3)
    .CircuitBreaker(o => { o.FailureRatio = 0.5; o.MinimumThroughput = 20; o.BreakDuration = TimeSpan.FromSeconds(30); });

var response = await policy.ExecuteAsync(ct => client.GetAsync(url, ct), cancellationToken);
```

Note the handling clause: written once, it covers the retry *and* the breaker. In Polly each strategy carries its own `ShouldHandle` predicate.

## Semantic differences worth knowing

- **Retry defaults differ.** Polly's default is constant 2s delays with no jitter; Kevlar's is exponential-with-jitter from 250ms capped at 30s. If you relied on Polly's default timing, say so explicitly: `Policy.Retry(3, Backoff.Constant(TimeSpan.FromSeconds(2)))`.
- **Handling clauses are ambient.** A clause applies to every reactive strategy after it until replaced — usually what you want, but check pipelines that used different predicates per strategy. Write a new clause mid-chain to switch.
- **Default handling excludes cancellation.** With no clause at all, Kevlar handles any exception except `OperationCanceledException` — same spirit as Polly's recommended predicate, but built in.
- **One policy, every shape.** There's no separate sync/async pipeline type: `policy.Execute(...)` and `policy.ExecuteAsync(...)` are the same instance. (Hedging is async-only, as in Polly.)
- **State sharing is by instance.** Like Polly, strategy state (circuits, buckets) lives with the built policy object — reuse the instance to share it. `Wrap`/`Compose` reference, not copy. See [Composition](composition.md#the-state-sharing-rule).
