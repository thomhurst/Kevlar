---
sidebar_position: 6
---

# Hedging

Race parallel attempts against tail latency: if the first attempt hasn't answered within the hedge delay, fire a second one. Fastest success wins; losers are cancelled.

Unlike a [retry](retry.md), hedging doesn't wait for the first attempt to *fail* — it launches a backup while the original is still running. The pattern goes by several names: **backup requests** (Google's [Tail at Scale](https://research.google/pubs/the-tail-at-scale/) calls them *hedged requests*), **speculative retry** (Cassandra), or **speculative execution**. Same idea everywhere: spend a little duplicate work to cut p99 latency.

```csharp
// Fire a second attempt if the first hasn't answered within 100ms.
Shield.Hedge(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(100));

Shield.Hedge(o =>
{
    o.MaxAttempts = 2;                        // default 2 (total attempts, incl. the first)
    o.Delay = TimeSpan.FromSeconds(1);        // default 1s
    o.OnHedge = e => logger.LogInformation("Hedge attempt {Attempt}", e.Attempt);
});
```

## Options

| Option | Default | What it does |
|---|---|---|
| `MaxAttempts` | `2` | Total attempts, including the first |
| `Delay` | `1s` | Wait before launching the next attempt (see special values below) |
| `OnHedge` | — | Callback when a hedge launches — `e.Attempt` is 1-based, so `2` = first hedge |

### Special delay values

- `TimeSpan.Zero` — race **all** attempts at once.
- `Timeout.InfiniteTimeSpan` — never hedge on latency; launch the next attempt **only when the previous one fails**.
- Any delay: a handled failure always launches the next attempt *immediately*, without waiting out the rest of the delay.

## The rules

:::warning Your delegate runs concurrently
Multiple invocations of your delegate may be in flight at once — it must be safe to invoke concurrently. This is also why **hedging requires async execution**: synchronous `Execute` throws `NotSupportedException`.
:::

- Losing attempts are cancelled through their token (use the token you're handed!).
- Caller cancellation prevents any later hedge from launching, even when it races a completed stagger delay. `OnHedge` runs only for attempts that actually launch.
- Each attempt gets a forked context — `Properties` are copied at launch time, so attempts don't see each other's writes.
- What counts as a failure is the ambient [handling clause](../handling-failures.md), like every reactive strategy.

## When to hedge (and when not to)

Hedging trades extra load for lower tail latency. It shines for:

- **Idempotent reads** against replicated backends — the second replica probably isn't having the same GC pause.
- Latency SLOs where p99 matters more than average cost.

Avoid it for writes that aren't idempotent (you may execute them twice!) and for dependencies that are slow because they're *overloaded* — hedging feeds the overload. Pair it with a [circuit breaker](circuit-breaker.md) or [rate limit](rate-limit.md) when in doubt:

```csharp
var shield = Shield
    .Timeout(TimeSpan.FromSeconds(2))
    .Hedge(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(100))
    .CircuitBreaker(o => o.FailureRatio = 0.5);
```
