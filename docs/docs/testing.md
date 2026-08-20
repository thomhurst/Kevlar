---
sidebar_position: 10
---

# Testing Your Policies

Every delay, timeout and time window in Kevlar runs on a `TimeProvider`. Swap in a fake and your tests never actually wait:

```csharp
using Microsoft.Extensions.Time.Testing;   // Microsoft.Extensions.TimeProvider.Testing package

var time = new FakeTimeProvider();
var policy = Policy
    .Retry(3, Backoff.Constant(TimeSpan.FromSeconds(10)))
    .WithTimeProvider(time);

var pending = policy.ExecuteAsync(ct => FlakyAsync(ct)).AsTask();

time.Advance(TimeSpan.FromSeconds(10));   // first retry delay elapses instantly
time.Advance(TimeSpan.FromSeconds(10));   // second
time.Advance(TimeSpan.FromSeconds(10));   // third

var result = await pending;
```

`WithTimeProvider` returns a new policy (policies are immutable) with every time-dependent strategy — retry backoff, timeouts, circuit breaker break durations and sampling windows, rate-limit windows, hedging delays — driven by the provider you supply.

:::warning Advance *after* the execution is in flight
Start the execution first (note the `.AsTask()` without `await`), *then* advance time. If you advance before the policy has scheduled its delay, there's nothing to advance past and the pending task will hang waiting for a tick that already happened.
:::

## Testing circuit breakers

Drive the breaker through its state machine without real clocks:

```csharp
var time = new FakeTimeProvider();
var monitor = new CircuitBreakerMonitor();
var policy = Policy
    .CircuitBreaker(consecutiveFailures: 2, breakDuration: TimeSpan.FromSeconds(30))
    .WithTimeProvider(time);

// Trip it:
for (var i = 0; i < 2; i++)
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => policy.ExecuteAsync(ct => throw new InvalidOperationException()).AsTask());

// Now open — rejects instantly:
await Assert.ThrowsAsync<CircuitOpenException>(
    () => policy.ExecuteAsync(ct => SucceedAsync(ct)).AsTask());

// After the break duration, one probe is allowed:
time.Advance(TimeSpan.FromSeconds(30));
var result = await policy.ExecuteAsync(ct => SucceedAsync(ct));   // closes the circuit
```

## Forcing outcomes without a real dependency

Policies don't care where the delegate's failures come from, so a plain counter is usually all the fake you need:

```csharp
var calls = 0;
var policy = Policy.Retry(2);

var result = await policy.ExecuteAsync(ct =>
{
    calls++;
    if (calls < 3) throw new HttpRequestException("boom");
    return new ValueTask<string>("ok");
});

// calls == 3: initial attempt + 2 retries
```

For assertion-friendly, no-throw checks, use [`ExecuteOutcomeAsync`](executing.md#no-throw-execution) and inspect the `Outcome<T>` instead of catching.
