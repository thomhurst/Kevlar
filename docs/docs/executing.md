---
sidebar_position: 6
---

# Executing

One policy instance serves any result type, sync or async:

```csharp
await policy.ExecuteAsync(ct => FetchAsync(ct), cancellationToken);   // async with result
await policy.ExecuteAsync(ct => SaveAsync(ct), cancellationToken);    // async void
policy.Execute(ct => ComputeSync(ct));                                // sync (same policy!)
```

The only exception: [hedging](strategies/hedging.md) is inherently concurrent and requires async execution.

:::tip Always use the token you're handed
Your delegate receives a `CancellationToken` that combines your outer token with policy-driven cancellation (timeouts, hedging losers). Pass it to everything you await.
:::

## Zero-closure hot paths

Capturing locals in a lambda allocates a closure on every call. On hot paths, thread your state through instead:

```csharp
await policy.ExecuteAsync(
    (client, id),                                     // your state, as a tuple
    static (s, ct) => s.client.GetUserAsync(s.id, ct), // static lambda: nothing captured
    cancellationToken);
```

The `static` keyword makes the compiler enforce it: this call site allocates nothing for the delegate.

## No-throw execution

When a failure is an expected outcome rather than an exceptional one, skip the throw/catch entirely and inspect the outcome:

```csharp
Outcome<User> outcome = await policy.ExecuteOutcomeAsync(ct => LoadAsync(ct));

if (!outcome.IsSuccess)
{
    logger.LogError(outcome.Exception, "gave up loading user");
    return cached;
}

return outcome.Result;
```

This is also how failures travel *between* strategies internally — as `Outcome<T>` structs, not thrown exceptions — which is a big part of why the pipeline is cheap. `ExecuteOutcomeAsync` just hands you the same struct instead of unwrapping it.

When an exception does surface from `ExecuteAsync`/`Execute`, the original stack trace is preserved (`ExceptionDispatchInfo`) — it's thrown once, at the boundary.
