---
sidebar_position: 2
---

# Circuit Breaker

Stop hammering a dependency that's already failing. The breaker measures failures and, once a threshold is crossed, rejects executions outright for a break duration — giving the dependency room to recover.

## Two modes

```csharp
// Simple: open after N consecutive failures
Shield.CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));

// Sampling: open when ≥50% of calls fail within a rolling window
Shield.CircuitBreaker(o =>
{
    o.FailureRatio = 0.5;
    o.MinimumThroughput = 20;
    o.SamplingWindow = TimeSpan.FromSeconds(30);
    o.BreakDuration = TimeSpan.FromSeconds(15);
});
```

Configure either `ConsecutiveFailures` *or* `FailureRatio` — not both. When neither is set, the breaker trips after 5 consecutive failures.

## Options

| Option | Default | What it does |
|---|---|---|
| `ConsecutiveFailures` | — | Simple mode: open after this many failures in a row |
| `FailureRatio` | — | Sampling mode: open when this fraction of calls fail (0 exclusive to 1 inclusive) |
| `MinimumThroughput` | `10` | Sampling mode: don't judge until at least this many calls landed in the window |
| `SamplingWindow` | `30s` | Rolling window over which the ratio is measured (tracked in 10 buckets) |
| `BreakDuration` | `15s` | How long the circuit stays open before allowing a probe |
| `BreakDurationGenerator` | — | Awaited outcome/context-aware duration; overrides `BreakDuration` for each trip |
| `Monitor` | — | A `CircuitBreakerMonitor` for observing + manual control |
| `OnStateChanged` | — | Callback on every transition: `e.From`, `e.To`, `e.LastException` |
| `OnStateChangedAsync` | — | Awaited callback on every transition, after `OnStateChanged` |
| `HandlesException` | — | Local exception predicate; replaces the ambient clause for this breaker |
| `HandlesResult` (`CircuitBreakerOptions<T>`) | — | Local result predicate on `Shield<T>`; replaces the ambient clause together with `HandlesException` |

### Dynamic break duration

Use `BreakDurationGenerator` when the dependency supplies a recovery hint or different failures need different cooling periods:

```csharp
var breaker = Shield.CircuitBreaker(o =>
{
    o.ConsecutiveFailures = 3;
    o.BreakDurationGenerator = async trip =>
    {
        await Task.Yield(); // for example, fetch a dependency recovery hint
        trip.Context.CancellationToken.ThrowIfCancellationRequested();
        return trip.Exception is TimeoutException
            ? TimeSpan.FromMinutes(1)
            : TimeSpan.FromSeconds(30);
    };
});
```

The generator runs after the handled outcome crosses the threshold and before the circuit changes to `Open`. It is awaited outside the circuit lock. Its duration must be positive; its exception or cancellation propagates unchanged and leaves the circuit available for a later trip. `Result` carries a boxed handled result when a typed shield trips without an exception. `Context` is pooled and must not be retained after the callback completes. A dynamic breaker describes itself as `break dynamic` without running the generator.

## The state machine

```
Closed ──(threshold crossed)──► Open ──(BreakDuration elapses)──► HalfOpen
  ▲                               ▲                                  │
  │                               │ probe fails                      │ probe succeeds
  └───────────────────────────────┴──────────────────────────────────┘
```

- **Closed** — executions flow normally; failures are measured.
- **Open** — executions are rejected immediately with `CircuitOpenException` (carrying `RetryAfter`: the time until a probe is allowed).
- **HalfOpen** — after the break duration, exactly **one** probe execution is allowed through. Success closes the circuit and resets metrics; failure re-opens it for another `BreakDuration`. Concurrent callers during the probe are rejected (`RetryAfter == null`).
- **Isolated** — manually forced open via the monitor; rejected until `Reset()`.

:::info Unhandled exceptions don't move the circuit
An exception outside the breaker's handling clause says nothing about downstream health — it counts
as neither success nor failure. This includes caller cancellation unless the clause explicitly
handles it. An unhandled half-open probe releases the probe slot without closing the circuit.
:::

## Observing and controlling: `CircuitBreakerMonitor`

```csharp
var monitor = new CircuitBreakerMonitor();
var shield = Shield.CircuitBreaker(o =>
{
    o.FailureRatio = 0.5;
    o.Monitor = monitor;
    o.OnStateChanged = c => logger.LogWarning("Circuit {From} -> {To}", c.From, c.To);
    o.OnStateChangedAsync = async c =>
    {
        await Task.Yield();
        logger.LogInformation("Recorded circuit transition to {State}", c.To);
    };
});

_ = monitor.State;      // Closed / Open / HalfOpen / Isolated
monitor.StateChanged += e => metrics.Record(e.To);
monitor.Isolate();      // force open (maintenance switch)
monitor.Reset();        // close and clear metrics
await monitor.IsolateAsync(); // async equivalents await transition callbacks
await monitor.ResetAsync();
```

A monitor binds to exactly **one** breaker: assign it to `CircuitBreakerOptions.Monitor` when building the shield, and keep your reference. Binding it twice throws, as does using it before binding.

Transitions are delivered serially in state-change order: `OnStateChanged`, awaited `OnStateChangedAsync`, then `monitor.StateChanged`. Callbacks run outside the circuit lock, so they can read `State` or call the synchronous or asynchronous monitor controls; a reentrant transition is queued behind the transition currently being delivered. Use `ResetAsync()` and `IsolateAsync()` when asynchronous transition callbacks are configured so the calling thread is not blocked. If an observer throws, later observers still run and the circuit keeps its new, usable state. One callback failure is rethrown unchanged after delivery; multiple failures are combined in an `AggregateException`.

## Share the circuit deliberately

A breaker only protects a dependency if every call site hitting that dependency shares it. State lives with the shield instance:

```csharp
var breaker = Shield.CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));   // ONE circuit
var reads   = Shield.Retry(3).Wrap(breaker);
var writes  = Shield.Timeout(TimeSpan.FromSeconds(5)).Wrap(breaker);
// failures through either trip both
```

See [Composition](../composition.md#the-state-sharing-rule).

For independent breaker state per tenant or endpoint, create the breaker inside a
[partitioned shield](../partitioning.md).

## What counts as a failure

Whatever the current [handling clause](../handling-failures.md) says — including handled *results* on typed shields, so an HTTP breaker can trip on 5xx responses without a single exception being thrown.

For one breaker with different rules, set `HandlesException` or `HandlesResult` in its options.
This [local override](../handling-failures.md#per-strategy-overrides) replaces, rather than extends,
the ambient clause.
