---
sidebar_position: 3
---

# Timeout

Bound how long an execution may take.

```csharp
Policy.Timeout(TimeSpan.FromSeconds(10));

Policy.Timeout(o =>
{
    o.Timeout = TimeSpan.FromSeconds(10);          // default 30s
    o.OnTimeout = e => logger.LogWarning("Timed out after {Timeout}", e.Timeout);
});
```

Exceeding the budget surfaces `TimeoutExceededException` (with a `Timeout` property).

## Timeouts are cooperative

The timeout doesn't kill your code — it cancels a token and expects your delegate to honour it:

```csharp
await policy.ExecuteAsync(ct => httpClient.GetAsync(url, ct), cancellationToken);
//                         ^^ always use the token you're handed
```

Internally, the timeout strategy swaps `context.CancellationToken` for a linked token that fires when the budget elapses. That's why the rule from [Executing](../executing.md) matters: a delegate that ignores its token can't be timed out.

Two behaviours worth knowing:

- If your delegate completes successfully *despite* the token firing, the result is still delivered — only an execution that ends in `OperationCanceledException` is converted to `TimeoutExceededException`.
- Cancellation from your own outer token is **not** reported as a timeout — it propagates as a normal `OperationCanceledException`.

## Total vs per-attempt

The classic pattern — position determines meaning:

```csharp
Policy
    .Timeout(TimeSpan.FromSeconds(30))   // TOTAL budget: all retries must fit inside
    .Retry(3)
    .Timeout(TimeSpan.FromSeconds(5));   // PER-ATTEMPT budget: each try gets 5s
```

The inner timeout's `TimeoutExceededException` is a handleable failure, so the retry sees it and tries again:

```csharp
Policy.Handle<TimeoutExceededException>().Retry(2).Timeout(TimeSpan.FromSeconds(5));
```
