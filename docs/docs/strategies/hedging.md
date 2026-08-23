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
    o.OnHedgeAsync = static _ => ValueTask.CompletedTask;
});
```

## Options

| Option | Default | What it does |
|---|---|---|
| `MaxAttempts` | `2` | Total attempts, including the first |
| `Delay` | `1s` | Wait before launching the next attempt (see special values below) |
| `OnHedge` | — | Callback when a hedge launches — `e.Attempt` is 1-based, so `2` = first hedge |
| `OnHedgeAsync` | — | Awaited callback after `OnHedge` and before the attempt starts |
| `ActionGenerator` | — | Select a different operation for each additional attempt; `null` uses the original |
| `HandlesException` | — | Local exception predicate; replaces the ambient clause for this hedge |
| `HandlesResult` (`HedgingOptions<T>`) | — | Local result predicate on `Shield<T>`; replaces the ambient clause together with `HandlesException` |

### Selecting another target

Use `HedgeActionGenerator.Create<TResult>` to send a hedge to a different replica without boxing
the result. The generator runs after both hedge callbacks and receives the isolated attempt context.
`OriginalAction` includes strategies nested inside the hedge, so returning it preserves the inner
pipeline. Returning another operation replaces that inner pipeline for that attempt.

```csharp
var replicas = new Func<CancellationToken, ValueTask<string>>[]
{
    static _ => new ValueTask<string>("primary"),
    static _ => new ValueTask<string>("secondary"),
    static _ => new ValueTask<string>("tertiary"),
};
var shield = Shield.For<string>().Hedge(o =>
{
    o.MaxAttempts = replicas.Length;
    o.Delay = TimeSpan.FromMilliseconds(100);
    o.ActionGenerator = HedgeActionGenerator.Create<string>(hedge =>
        ct => replicas[hedge.Attempt - 1](ct));
});

// The original callback is attempt 1; generated callbacks are attempts 2 and 3.
var response = await shield.ExecuteAsync(ct => replicas[0](ct));
```

For a void execution, use the non-generic `HedgeActionGenerator.Create` overload. A generator must
match the execution result type; a mismatch fails before the additional operation starts.

### Special delay values

- `TimeSpan.Zero` — race **all** attempts at once.
- `Timeout.InfiniteTimeSpan` — never hedge on latency; launch the next attempt **only when the previous one fails**.
- Any delay: a handled failure always launches the next attempt *immediately*, without waiting out the rest of the delay.

## The rules

:::warning Your delegate runs concurrently
Multiple invocations of your delegate may be in flight at once — it must be safe to invoke concurrently. This is also why **hedging requires async execution**: synchronous `Execute` throws `NotSupportedException`.
:::

:::warning Hedging on an untyped `Shield` needs an idempotent action
An untyped `Shield` can judge attempts only by their *exceptions*, so every attempt it launches runs
to completion against the real dependency. A losing hedge still did its work: duplicate writes,
charges, or sends are observable unless the action is idempotent. Prefer `Shield.For<T>()`, where a
[result clause](../handling-failures.md#result-clauses) decides which attempt is acceptable — or
confirm the action is safe to repeat. The [`KEV006` analyzer](../analyzers.md#kev006-hedging-on-an-untyped-shield)
flags untyped `Hedge(...)` for exactly this reason.
:::

- Losing attempts are cancelled through their token (use the token you're handed!).
- Caller cancellation prevents any later hedge delegate from running, even when it races a completed stagger delay or occurs inside `OnHedge`/`OnHedgeAsync`. A cancellation already observable at the launch boundary suppresses both callbacks.
- Launch ordering is `OnHedge`, awaited `OnHedgeAsync`, action generation, metrics, then the selected operation. Callback or generator failures preserve their exception identity, cancel in-flight attempts, and are not counted as launched hedges.
- Each attempt gets a forked context — `Properties` are copied at launch time, so attempts don't see each other's writes.
- Callback contexts are pooled. Do not retain them after the synchronous callback or returned `ValueTask` completes; a generated action's isolated context remains valid until that attempt completes.
- Callbacks and generators run without a strategy lock. They may re-enter the same shield, and concurrent shield executions may invoke them concurrently; keep captured state thread-safe.
- Losing operations are cancelled first, then their isolated contexts are returned to the pool after those operations complete.
- What counts as a failure is the ambient [handling clause](../handling-failures.md), unless the
  options set `HandlesException` or `HandlesResult` as a
  [per-strategy override](../handling-failures.md#per-strategy-overrides).

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
