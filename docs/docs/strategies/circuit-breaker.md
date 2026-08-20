---
sidebar_position: 2
---

# Circuit Breaker

Stop hammering a dependency that's already failing. The breaker measures failures and, once a threshold is crossed, rejects executions outright for a break duration — giving the dependency room to recover.

## Two modes

```csharp
// Simple: open after N consecutive failures
Policy.CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));

// Sampling: open when ≥50% of calls fail within a rolling window
Policy.CircuitBreaker(o =>
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
| `Monitor` | — | A `CircuitBreakerMonitor` for observing + manual control |
| `OnStateChanged` | — | Callback on every transition: `e.From`, `e.To`, `e.LastException` |

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

:::info Cancellation doesn't move the circuit
A cancelled execution says nothing about downstream health — it counts as neither success nor failure.
:::

## Observing and controlling: `CircuitBreakerMonitor`

```csharp
var monitor = new CircuitBreakerMonitor();
var policy = Policy.CircuitBreaker(o =>
{
    o.FailureRatio = 0.5;
    o.Monitor = monitor;
    o.OnStateChanged = c => logger.LogWarning("Circuit {From} -> {To}", c.From, c.To);
});

monitor.State;          // Closed / Open / HalfOpen / Isolated
monitor.StateChanged += e => metrics.Record(e.To);
monitor.Isolate();      // force open (maintenance switch)
monitor.Reset();        // close and clear metrics
```

A monitor binds to exactly **one** breaker: assign it to `CircuitBreakerOptions.Monitor` when building the policy, and keep your reference. Binding it twice throws, as does using it before binding. Both `OnStateChanged` and `monitor.StateChanged` fire for the same transition (callback first).

## Share the circuit deliberately

A breaker only protects a dependency if every call site hitting that dependency shares it. State lives with the policy instance:

```csharp
var breaker = Policy.CircuitBreaker(5, TimeSpan.FromSeconds(30));   // ONE circuit
var reads   = Policy.Retry(3).Wrap(breaker);
var writes  = Policy.Timeout(TimeSpan.FromSeconds(5)).Wrap(breaker);
// failures through either trip both
```

See [Composition](../composition.md#the-state-sharing-rule).

## What counts as a failure

Whatever the current [handling clause](../handling-failures.md) says — including handled *results* on typed policies, so an HTTP breaker can trip on 5xx responses without a single exception being thrown.
