---
sidebar_position: 3
---

# Timeout

Bound how long an execution may take.

```csharp
Shield.Timeout(TimeSpan.FromSeconds(10));

Shield.Timeout(o =>
{
    o.Timeout = TimeSpan.FromSeconds(10);          // default 30s
    o.OnTimeout = e => logger.LogWarning("Timed out after {Timeout}", e.Timeout);
    o.OnTimeoutAsync = e => ValueTask.CompletedTask;
});
```

Exceeding the budget surfaces `TimeoutExceededException` (with a `Timeout` property). It derives from
`KevlarException`, **not** from `System.TimeoutException`, so a reflexive `catch (TimeoutException)`
compiles but never matches — catch `TimeoutExceededException` (or `KevlarException` for any Kevlar
rejection) instead.

## Timeouts are cooperative

The timeout doesn't kill your code — it cancels a token and expects your delegate to honour it:

```csharp
await shield.ExecuteAsync(ct => httpClient.GetAsync(url, ct), cancellationToken);
//                         ^^ always use the token you're handed
```

Internally, the timeout strategy swaps `context.CancellationToken` for a linked token that fires when the budget elapses. That's why the rule from [Executing](../executing.md) matters: a delegate that ignores its token can't be timed out.

Two behaviours worth knowing:

- If your delegate completes successfully *despite* the token firing, the result is still delivered.
- Cancellation from your own outer token is **not** reported as a timeout — it propagates as a normal `OperationCanceledException`.

## Cancellation arbitration

When cancellation signals overlap, the outcome is decided after the delegate completes:

1. If the caller's token is cancelled, caller cancellation wins. The resulting `OperationCanceledException` carries the caller's token.
2. Otherwise, if the timeout fired and the delegate's `OperationCanceledException` carries the exact token handed to the delegate, the outcome becomes `TimeoutExceededException`.
3. An `OperationCanceledException` for any other token is preserved unchanged.

This token-identity rule also applies to nested timeouts. A cancelled outer timeout is treated as the inner timeout's caller, so only the winning scope reports `OnTimeout`. The strategy restores the prior context token and completes timer cleanup before invoking `OnTimeout`; if the callback throws, that exception is surfaced without contaminating later executions.

## Dynamic timeouts and asynchronous notifications

Use `TimeoutGenerator` when the budget depends on the current execution context. The generator runs
and is awaited before the timer is armed or the executed delegate starts. Its result overrides the
fixed `Timeout` for that execution and must be positive and within the runtime timer limit:

```csharp
var shield = Shield.Timeout(o =>
{
    o.TimeoutGenerator = context => new ValueTask<TimeSpan>(
        context.ShieldName == "interactive"
            ? TimeSpan.FromSeconds(2)
            : TimeSpan.FromSeconds(30));

    o.OnTimeout = timeout => Console.WriteLine(timeout.Timeout);
    o.OnTimeoutAsync = timeout => ValueTask.CompletedTask;
});
```

The generator receives the caller's current `CancellationToken` through `KevlarContext`. Observe it
in asynchronous work. If caller cancellation wins while generation runs, the executed delegate is
not started. The context is pooled: inspect it only during the generator or callback and never retain
it.

After a timeout wins, Kevlar records timeout metrics, restores the caller token, disposes timer state,
then invokes `OnTimeout` followed by awaited `OnTimeoutAsync`. A callback exception or cancellation
is surfaced unchanged instead of `TimeoutExceededException`. Hooks may run concurrently when a shield
is reused, so callbacks must be thread-safe and must not assume serialization. Truly asynchronous
generators and hooks block the calling thread when used through synchronous `Execute`.

## Total vs per-attempt

The classic pattern — position determines meaning:

```csharp
Shield
    .Timeout(TimeSpan.FromSeconds(30))   // TOTAL budget: all retries must fit inside
    .Retry(3)
    .Timeout(TimeSpan.FromSeconds(5));   // PER-ATTEMPT budget: each try gets 5s
```

The inner timeout's `TimeoutExceededException` is a handleable failure, so the retry sees it and tries again:

```csharp
Shield.When<TimeoutExceededException>().Retry(2).Timeout(TimeSpan.FromSeconds(5));
```
