---
sidebar_position: 2
---

# Circuit Breaker

Stop hammering a dependency that's already failing. The breaker measures failures and, once a threshold is crossed, rejects executions outright for a break duration — giving the dependency room to recover.

See the [exceptions reference](../exceptions.md) for `CircuitOpenException` and its recovery metadata.

## Two modes

```csharp
// Simple: open after N consecutive failures
var consecutive = Shield.CircuitBreaker(
    consecutiveFailures: 5,
    breakDuration: TimeSpan.FromSeconds(30));

// Sampling: open when ≥50% of calls fail within a rolling window
var sampling = Shield.CircuitBreaker(o =>
{
    o.FailureRatio = 0.5;
    o.MinimumThroughput = 20;
    o.SamplingWindow = TimeSpan.FromSeconds(30);
    o.BreakDuration = TimeSpan.FromSeconds(15);
});
```

Configure either `ConsecutiveFailures` *or* `FailureRatio` — not both. When neither is set, the breaker trips after 5 consecutive failures.

## Options

API reference: [`CircuitBreakerOptions`](pathname:///api/Kevlar.CircuitBreakerOptions.html) and [`CircuitBreakerOptions<T>`](pathname:///api/Kevlar.CircuitBreakerOptions-1.html).

<div style={{overflowX: 'auto'}}>

| Option | Default | What it does |
|---|---|---|
| `ConsecutiveFailures` | — | Simple mode: open after this many failures in a row |
| `FailureRatio` | — | Sampling mode: open when this fraction of calls fail (0 exclusive to 1 inclusive) |
| `HalfOpenProbes` | `1` | Completed outcomes per half-open cohort and maximum concurrent probes |
| `SlowCallThreshold` | — | Calls taking strictly longer than this duration count as slow; requires sampling mode and `SlowCallRatio` |
| `SlowCallRatio` | — | Open when this fraction of sampled calls is slow (0 exclusive to 1 inclusive) |
| `MinimumThroughput` | `10` | Sampling mode: don't judge until at least this many calls landed in the window |
| `SamplingWindow` | `30s` | Rolling window over which the ratio is measured (tracked in 10 buckets) |
| `BreakDuration` | `15s` | How long the circuit stays open before allowing a probe |
| `BreakDurationGenerator` | — | Awaited outcome, failure-statistics, and context-aware duration returning `ValueTask<TimeSpan>`; overrides `BreakDuration` for each trip |
| `Monitor` | — | A `CircuitBreakerMonitor` for observing + manual control |
| `OnStateChanged` | — | Awaited callback on every transition: `e.From`, `e.To`, `e.LastException`, `e.Context` |
| `HandlesException` | — | Local exception predicate; replaces the ambient clause for this breaker |
| `HandlesResult` (`CircuitBreakerOptions<T>`) | — | Local result predicate on `Shield<T>`; replaces the ambient clause together with `HandlesException` |

</div>

Invalid option values throw [`KevlarConfigurationException`](../exceptions.md#configuration-failures)
and identify the options type, property, and offending value. This also applies when
`BreakDurationGenerator` returns a non-positive duration.

### Recovery probes and slow calls

Increase `HalfOpenProbes` to evaluate recovery using several calls. Each half-open cohort admits
at most that many calls, including already completed probes. Simple mode reopens on its first
handled failure. Sampling mode reopens when failures divided by `HalfOpenProbes` reach
`FailureRatio`; this permits an occasional failed probe below the threshold. The circuit closes
only after the whole cohort completes below every configured threshold. Unhandled exceptions and
caller cancellation release a slot for a replacement without counting as completed observations.

Slow-call detection measures the downstream execution using the shield's `TimeProvider`. It
observes latency without cancelling the call or changing its result:

```csharp
var latencyAware = Shield.CircuitBreaker(options =>
{
    options.FailureRatio = 0.5;
    options.MinimumThroughput = 20;
    options.SamplingWindow = TimeSpan.FromSeconds(30);
    options.HalfOpenProbes = 4;
    options.SlowCallThreshold = TimeSpan.FromMilliseconds(250);
    options.SlowCallRatio = 0.75;
});
```

Set both slow-call options together. The failure ratio and slow-call ratio are independent trip
conditions over the same window and minimum throughput. A call that both fails and is slow counts
once in the total, once in each applicable numerator. Successful calls and handled failures are
measured; unhandled exceptions and caller cancellation remain excluded. In half-open state, slow
probes divided by `HalfOpenProbes` are checked against `SlowCallRatio`, independently of
`MinimumThroughput`. An opening caused by slow successful calls can have no `LastException`.

`Kevlar.Testing` exposes `ProbesInFlight` and `SlowCallCount` through
`GetStateSnapshot()` and `CircuitBreakerStateSnapshot`. The slow count covers the current
closed-state sampling window; half-open observations are evaluated separately. Closing or resetting
the circuit clears the window. `GetDescriptor()` exposes the three configured settings, and
`CircuitBreakerDefinition` accepts the same fields when binding configuration.

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
        Console.WriteLine($"Opening at {trip.FailureRate:P0} failures");
        return trip.Exception is TimeoutExceededException
            ? TimeSpan.FromMinutes(1)
            : TimeSpan.FromSeconds(30);
    };
});
```

The generator runs after a handled failure or slow-call observation crosses a threshold and before the circuit changes
to `Open`. It receives `FailureRate`, `FailureCount`, and `ConsecutiveFailures` at that moment and
is awaited outside the circuit lock. Its duration must be positive; its exception or cancellation
propagates unchanged and leaves the circuit available for a later trip. Untyped shields expose
`Exception` and boxed `Result`; `CircuitBreakerOptions<T>` instead receives
`CircuitBreakerBreakDurationEvent<T>` with a directly stored `Outcome<T>`. `Context` is pooled and
must not be retained after the callback completes. A dynamic breaker describes itself as
`break dynamic` without running the generator.

Failure statistics continue to describe handled failures when a slow-call threshold triggers the
generator. While a half-open duration generator is pending, new probes are rejected and other
completions cannot close the circuit. If generation fails, completed observations are discarded;
still-running probes retain their slots and replacements may fill the remaining cohort.

When duration computation is synchronous, return a completed value (`trip => new(duration)`); that
form costs nothing extra and works with synchronous `Execute`. A generator or `OnStateChanged` hook
that yields is awaited by `ExecuteAsync`, but reached through synchronous `Execute` it throws
`NotSupportedException` at that call. See
[synchronous execution compatibility](../executing.md#synchronous-execution-compatibility).

## The state machine

```
Closed ──(threshold crossed)──► Open ──(BreakDuration elapses)──► HalfOpen
  ▲                               ▲                                  │
  │                               │ probe fails                      │ probe succeeds
  └───────────────────────────────┴──────────────────────────────────┘
```

- **Closed** — executions flow normally; failures and configured slow calls are measured.
- **Open** — executions are rejected immediately with `CircuitOpenException` (carrying `RetryAfter`: the time until a probe is allowed).
- **HalfOpen** — after the break duration, up to `HalfOpenProbes` calls are admitted (default **one**). A healthy completed cohort closes the circuit and resets metrics; reaching a configured failure or slow-call threshold reopens it. Calls beyond the cohort limit are rejected (`RetryAfter == null`).
- **Isolated** — manually forced open via the monitor; rejected until `Reset()`.

After the break duration elapses, `CircuitBreakerMonitor.State` reports `HalfOpen` immediately,
even before another execution arrives. Admission remains lazy: the actual `Open` → `HalfOpen`
transition and its state-change callbacks occur when the next execution claims the probe slot.
Executions admitted before the current open/half-open generation cannot later close or re-open the
circuit. The exception that opened the circuit is retained for open rejections, then released when
the circuit closes or is reset.

:::info Unhandled exceptions don't move the circuit
An exception outside the breaker's handling clause says nothing about downstream health — it counts
as neither success nor failure. Caller cancellation is always unhandled while the caller's token is
signaled, even when the clause matches `OperationCanceledException`. An unhandled half-open probe
releases the probe slot without closing the circuit.
:::

## Observing and controlling: `CircuitBreakerMonitor`

```csharp
var monitor = new CircuitBreakerMonitor();
var shield = Shield.CircuitBreaker(o =>
{
    o.FailureRatio = 0.5;
    o.Monitor = monitor;
    o.OnStateChanged = async c =>
    {
        logger.LogWarning("Circuit {From} -> {To}", c.From, c.To);
        await Task.Yield(); // for example, publish the transition to an audit sink
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

A monitor can bind to **one or more** breakers: assign it to each
`CircuitBreakerOptions.Monitor` you want to control, and keep your reference. `Isolate` and
`Reset` fan out to every bound breaker. `State` reports the worst current state, ordered
`Isolated`, `Open`, `HalfOpen`, then `Closed`. Using a monitor before binding still throws.

Each breaker raises its own events. Transitions are delivered serially per breaker in state-change
order: awaited `OnStateChanged`, then `monitor.StateChanged`. Different breakers bound to the same
monitor may publish concurrently. The monitor intentionally retains an
`Action<CircuitBreakerStateChangedEvent>` event so both observers share one event shape and
delivery model. Execution-driven transitions carry the triggering pooled context. Manual
`Isolate`/`Reset` transitions carry a detached context with `StrategyIndex == -1`, no shield name,
and an empty property bag. Callbacks run outside the circuit lock, so they can read `State` or call
the synchronous or asynchronous monitor controls; a reentrant transition is queued behind the
transition currently being delivered. Use `ResetAsync()` and `IsolateAsync()` when `OnStateChanged`
may yield so the calling thread is not blocked; these methods await every bound breaker in binding
order. Transition publication is serialized per breaker. A publisher arriving while another thread
drains observers waits for that drain; an execution can therefore occupy a thread-pool thread until
the earlier handlers return. If an observer throws,
later observers still run and the circuit keeps its new, usable state. Observer failures are
reported through `KevlarDiagnostics.OnCallbackError` under the shared
[callback-failure contract](../observability.md#callback-failures) and never replace an execution
outcome or block a transition.

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
