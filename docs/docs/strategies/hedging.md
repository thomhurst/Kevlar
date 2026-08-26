---
sidebar_position: 6
---

# Hedging

Race parallel attempts against tail latency: if the first attempt hasn't answered within the hedge delay, fire a second one. Fastest success wins; losers are cancelled.

See the [exceptions reference](../exceptions.md) for how failures and cancellation surface.

Unlike a [retry](retry.md), hedging doesn't wait for the first attempt to *fail* — it launches a backup while the original is still running. The pattern goes by several names: **backup requests** (Google's [Tail at Scale](https://research.google/pubs/the-tail-at-scale/) calls them *hedged requests*), **speculative retry** (Cassandra), or **speculative execution**. Same idea everywhere: spend a little duplicate work to cut p99 latency.

```csharp
// Fire a second attempt if the first hasn't answered within 100ms.
Shield.Hedge(maxHedgedAttempts: 1, delay: TimeSpan.FromMilliseconds(100));

Shield.Hedge(o =>
{
    o.MaxHedgedAttempts = 1;                  // default 1 (plus the original attempt)
    o.Delay = TimeSpan.FromSeconds(1);         // default 1s
    o.OnHedge = e =>
    {
        logger.LogInformation("Hedge attempt {AttemptNumber}", e.AttemptNumber);
        return default;
    };
});
```

## Options

API reference: [`HedgeOptions`](pathname:///api/Kevlar.HedgeOptions.html) and [`HedgeOptions<T>`](pathname:///api/Kevlar.HedgeOptions-1.html).

| Option | Default | What it does |
|---|---|---|
| `MaxHedgedAttempts` | `1` | Maximum additional attempts after the original |
| `Delay` | `1s` | Wait before launching the next attempt (see special values below) |
| `DelayGenerator` | — | Awaited selector returning `ValueTask<TimeSpan>`: a delay for each pending hedge from its attempt number, context, and elapsed execution time |
| `OnHedge` | — | Awaited callback when a hedge launches, before the attempt starts — `e.AttemptNumber` is a 1-based execution number, so `2` = first hedge |
| `ActionGenerator` | — | Select a different operation for each additional attempt; `null` uses the original |
| `HandlesException` | — | Local exception predicate; replaces the ambient clause for this hedge |
| `HandlesResult` (`HedgeOptions<T>`) | — | Local result predicate on `Shield<T>`; replaces the ambient clause together with `HandlesException` |

Invalid option values throw [`KevlarConfigurationException`](../exceptions.md#configuration-failures)
and identify the options type, property, and offending value.

### Selecting another target

Set the typed `ActionGenerator` to send a hedge to a different replica without boxing the result.
The generator runs after both hedge callbacks and receives the isolated attempt context plus the
latest handled outcome, when one is available. `OriginalAction` includes strategies nested inside
the hedge, so returning it preserves the inner pipeline. Returning another operation replaces that
inner pipeline for that attempt.

```csharp
var replicas = new Func<CancellationToken, ValueTask<string>>[]
{
    static _ => new ValueTask<string>("primary"),
    static _ => new ValueTask<string>("secondary"),
    static _ => new ValueTask<string>("tertiary"),
};
var shield = Shield.For<string>().Hedge(o =>
{
    o.MaxHedgedAttempts = replicas.Length - 1;
    o.Delay = TimeSpan.FromMilliseconds(100);
    o.ActionGenerator = hedge =>
        ct => replicas[hedge.AttemptNumber - 1](ct);
});

// The original callback is attempt 1; generated callbacks are attempts 2 and 3.
var response = await shield.ExecuteAsync(ct => replicas[0](ct));
```

For an untyped or void shield, use `HedgeActionGenerator.Create`. Lifting an untyped generator into
a shield with a different result type fails while that typed shield is built.

### Adaptive delays

Use a delay generator when each execution or hedge needs different timing. Generator delays are
relative to the previous hedge launch, so `100ms` followed by `300ms` launches attempts 2 and 3 at
approximately 100ms and 400ms. A handled failure still launches the next attempt immediately.

```csharp
var hedgeDelay = new KevlarKey<TimeSpan>("hedge-delay");
var adaptiveHedge = Shield.Hedge(options =>
{
    options.MaxHedgedAttempts = 2;
    options.DelayGenerator = hedge => new(
        hedge.Context.Properties.GetOrDefault(hedgeDelay, TimeSpan.FromMilliseconds(100)));
});

var adaptiveResult = await adaptiveHedge.ExecuteWithContextAsync(
    TimeSpan.FromMilliseconds(75),
    (delay, properties) => properties.Set(hedgeDelay, delay),
    static (_, _) => new ValueTask<int>(42));
```

`HedgeDelayEvent.AttemptNumber` is the 1-based execution number (`2` = first hedge), and `Elapsed`
is measured from the primary attempt's start through the shield's `TimeProvider`. Generated
negative delays become zero, values above the runtime timer limit are clamped, and the same special
zero/infinite meanings apply. Generator exceptions fail the execution and cancel in-flight attempts.

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
- Caller cancellation prevents any later hedge delegate from running, even when it races a completed stagger delay or occurs inside `DelayGenerator` or `OnHedge`. A cancellation already observable at the launch boundary suppresses callbacks.
- Launch ordering is the awaited `DelayGenerator` (while the previous attempt is pending), then awaited `OnHedge`, action generation, metrics, then the selected operation. Callback failures are reported through `KevlarDiagnostics.OnCallbackError` and do not suppress the launch. Generator failures preserve their exception identity, cancel in-flight attempts, and are not counted as launched hedges.
- Each attempt gets a forked context — `Properties` are copied at launch time, so attempts don't see each other's writes.
- Callback contexts are pooled. Do not retain them after the returned `ValueTask` completes; a generated action's isolated context remains valid until that attempt completes.
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
    .Hedge(maxHedgedAttempts: 1, delay: TimeSpan.FromMilliseconds(100))
    .CircuitBreaker(o => o.FailureRatio = 0.5);
```
