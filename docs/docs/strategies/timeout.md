---
sidebar_position: 3
---

# Timeout

Bound how long an execution may take.

See the [exceptions reference](../exceptions.md) for the exact timeout catch contract.
API reference: [`TimeoutOptions`](pathname:///api/Kevlar.TimeoutOptions.html).

```csharp
Shield.Timeout(TimeSpan.FromSeconds(10));

Shield.Timeout(o =>
{
    o.Timeout = TimeSpan.FromSeconds(10);          // default 30s
    o.OnTimeout = e =>
    {
        logger.LogWarning("Timed out after {Timeout}", e.Timeout);
        return default;
    };
});
```

Exceeding the budget surfaces `TimeoutExceededException` (with a `Timeout` property). It derives from
`KevlarException`, **not** from `System.TimeoutException`, so a reflexive `catch (TimeoutException)` <!-- doc-lint: allow-TimeoutException -->
compiles but never matches — catch `TimeoutExceededException` (or `KevlarException` for any Kevlar
strategy exception) instead.

## Timeouts are cooperative

The timeout doesn't kill your code — it cancels a token and expects your delegate to honour it:

```csharp
await shield.ExecuteAsync(ct => httpClient.GetAsync(url, ct), cancellationToken);
//                         ^^ always use the token you're handed
```

Internally, the timeout strategy swaps `context.CancellationToken` for a linked token that fires when the budget elapses. That's why the rule from [Executing](../executing.md) matters: a delegate that ignores its token can't be timed out.

Two behaviours worth knowing:

- If your delegate completes *despite* the token firing, its result or non-cancellation exception is
  still delivered. Kevlar emits a `timeout_ignored` Warning event containing the elapsed duration,
  increments `kevlar.timeouts` with `outcome=ignored`, and does not invoke `OnTimeout`.
- Cancellation from your own outer token is **not** reported as a timeout — it propagates as a normal `OperationCanceledException`.

## Cancellation arbitration

When cancellation signals overlap, the outcome is decided after the delegate completes:

1. If the caller's token is cancelled, caller cancellation wins. The resulting `OperationCanceledException` carries the caller's token.
2. Otherwise, if the timeout fired, any `OperationCanceledException` from the delegate becomes `TimeoutExceededException`; the original cancellation is available as `InnerException`.
3. If neither the caller nor timeout has fired, an `OperationCanceledException` is preserved unchanged.

This caller-first rule also applies to nested timeouts. A cancelled outer timeout is treated as the inner timeout's caller, so only the winning scope reports `OnTimeout`. The strategy restores the prior context token and completes timer cleanup before invoking `OnTimeout`; callback failures are reported through `KevlarDiagnostics.OnCallbackError` without replacing the timeout outcome.

## Dynamic timeouts and notifications

Use `TimeoutGenerator` to compute the budget per execution. It returns `ValueTask<TimeSpan>`, so
the same property serves a synchronous computation (`context => new(duration)`) and an `async`
one. The generator runs before the timer is armed or the executed delegate starts. Its result
overrides the fixed `Timeout` for that execution and must be positive and within the runtime timer
limit:

```csharp
var shield = Shield.Timeout(o =>
{
    o.TimeoutGenerator = context => new(
        context.ShieldName == "interactive"
            ? TimeSpan.FromSeconds(2)
            : TimeSpan.FromSeconds(30));

    o.OnTimeout = timeout =>
    {
        Console.WriteLine(timeout.Timeout);
        return default;
    };
});
```

The generator receives the caller's current `CancellationToken` through `KevlarContext`. Observe it
in asynchronous work. If caller cancellation wins while generation runs, the executed delegate is
not started. The context is pooled: inspect it only during the generator or callback and never retain
it.

Invalid `TimeoutOptions` values—and invalid durations returned by `TimeoutGenerator`—throw
[`KevlarConfigurationException`](../exceptions.md#configuration-failures) naming the property and
offending value.

After a timeout wins, Kevlar records timeout metrics, restores the caller token, disposes timer state,
then awaits `OnTimeout`. Callback exceptions and cancellations are reported through
`KevlarDiagnostics.OnCallbackError`; `TimeoutExceededException` remains the outcome. Hooks may run
concurrently when a shield is reused, so callbacks must be thread-safe and must not assume
serialization.

`TimeoutGenerator` and `OnTimeout` both return `ValueTask`. A hook that completes synchronously
(`return default;`, `new(value)`, `ValueTask.CompletedTask`) costs nothing extra and works with
synchronous `Execute`. A hook that yields is awaited by `ExecuteAsync`; reached through synchronous
`Execute`, it throws `NotSupportedException` at that call — Kevlar never blocks the calling thread
on a hook. See [synchronous execution compatibility](../executing.md#synchronous-execution-compatibility).

## Total vs per-attempt

The classic pattern — position determines meaning:

```csharp
Shield
    .Timeout(TimeSpan.FromSeconds(30))   // TOTAL budget: all retries must fit inside
    .Retry(3)
    .Timeout(TimeSpan.FromSeconds(5));   // PER-ATTEMPT budget: each try gets 5s
```

The inner timeout's `TimeoutExceededException` is a handleable failure, so the retry sees it and tries again:

<!-- doc-test-run: timeout-exceeded-clause -->
```csharp
using Kevlar.Testing;
using Microsoft.Extensions.Time.Testing;

var time = new FakeTimeProvider();
var attempts = 0;
var execution = Shield
    .When<TimeoutExceededException>()
    .Retry(1, Backoff.None)
    .Timeout(TimeSpan.FromSeconds(1))
    .WithTimeProvider(time)
    .ExecuteAsync(async token =>
    {
        Interlocked.Increment(ref attempts);
        var never = new TaskCompletionSource();
        using var registration = token.Register(() => never.SetCanceled(token));
        await never.Task;
    })
    .AsTask();

await ShieldExecution.WaitForPendingAsync(execution,
    () => Volatile.Read(ref attempts) == 1,
    "the first timed attempt");
await time.AdvanceUntilAsync(
    TimeSpan.FromSeconds(1),
    () => Volatile.Read(ref attempts) == 2,
    "the retry attempt",
    maxAdvances: 1);
await time.AdvanceUntilAsync(
    TimeSpan.FromSeconds(1),
    () => execution.IsCompleted,
    "the final timeout",
    maxAdvances: 1);

try
{
    await execution;
    throw new InvalidOperationException("Expected the retry pipeline to time out.");
}
catch (TimeoutExceededException) when (attempts == 2)
{
    // The clause handled the first timeout, so Retry made two total attempts.
}
```

The similar-looking `System.TimeoutException` clause does not handle Kevlar's timeout failure:

<!-- doc-test-run: system-timeout-clause-trap -->
```csharp
using Kevlar.Testing;
using Microsoft.Extensions.Time.Testing;

var time = new FakeTimeProvider();
var attempts = 0;
var shield = Shield
    .When<TimeoutException>() // <!-- doc-lint: allow-TimeoutException -->
    .Retry(1, Backoff.None)
    .Timeout(TimeSpan.FromSeconds(1))
    .WithTimeProvider(time);
var execution = shield.ExecuteAsync(async token =>
{
    Interlocked.Increment(ref attempts);
    var never = new TaskCompletionSource();
    using var registration = token.Register(() => never.SetCanceled(token));
    await never.Task;
}).AsTask();

await ShieldExecution.WaitForPendingAsync(execution,
    () => Volatile.Read(ref attempts) == 1,
    "the timed attempt");
await time.AdvanceUntilAsync(
    TimeSpan.FromSeconds(1),
    () => execution.IsCompleted,
    "the unhandled timeout",
    maxAdvances: 1);

try
{
    await execution;
    throw new InvalidOperationException("Expected the timeout to propagate.");
}
catch (TimeoutExceededException) when (attempts == 1)
{
    // No retry: System.TimeoutException is the wrong clause for Kevlar timeouts.
}
```
