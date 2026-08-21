---
sidebar_position: 6
---

# Executing

One shield instance serves any result type, sync or async:

```csharp
await shield.ExecuteAsync(ct => FetchAsync(ct), cancellationToken);   // async with result
await shield.ExecuteAsync(ct => SaveAsync(ct), cancellationToken);    // async void
shield.Execute(ct => ComputeSync(ct));                                // sync (same shield!)
```

The only exception: [hedging](strategies/hedging.md) is inherently concurrent and requires async execution.

`Task` and `ValueTask` delegates both work — your existing `Task`-returning methods flow straight in, no wrapping:

```csharp
Task<User> LoadUserAsync(int id, CancellationToken ct) => ...;   // ordinary Task method

var user = await shield.ExecuteAsync(ct => LoadUserAsync(id, ct), cancellationToken);
```

(Async lambdas bind to the `ValueTask` overloads automatically, so the hot path stays allocation-free.)

## Overload contract

Every execution shape follows the same boundary contract: typed or untyped shield, result or
void, synchronous or asynchronous, `Task` or `ValueTask`, and state-passing or capturing.

- A pre-cancelled caller token skips the delegate. Throwing execution surfaces an
  `OperationCanceledException` carrying that exact token; `ExecuteOutcomeAsync` captures it.
- Boundary adapters invoke the delegate once. Strategies such as retry and hedging may
  intentionally invoke it again according to their configuration.
- State-passing overloads pass the original state unchanged to the static delegate.
- Empty and non-empty pipelines use the same cancellation, result, and exception semantics.
- Throwing execution preserves the original exception instance and stack;
  `ExecuteOutcomeAsync` captures that same exception instead.

:::tip Always use the token you're handed
Your delegate receives a `CancellationToken` that combines your outer token with shield-driven cancellation (timeouts, hedging losers). Pass it to everything you await.
:::

## Zero-closure hot paths

Capturing locals in a lambda allocates a closure on every call. On hot paths, thread your state through instead:

```csharp
await shield.ExecuteAsync(
    (client, id),                                     // your state, as a tuple
    static (s, ct) => s.client.GetUserAsync(s.id, ct), // static lambda: nothing captured
    cancellationToken);
```

The `static` keyword makes the compiler enforce it: this call site allocates nothing for the delegate.

## No-throw execution

When a failure is an expected outcome rather than an exceptional one, skip the throw/catch entirely and inspect the outcome:

```csharp
Outcome<User> outcome = await shield.ExecuteOutcomeAsync(ct => LoadAsync(ct));

if (!outcome.IsSuccess)
{
    logger.LogError(outcome.Exception, "gave up loading user");
    return cached;
}

return outcome.Result;
```

This is also how failures travel *between* strategies internally — as `Outcome<T>` structs, not thrown exceptions — which is a big part of why the pipeline is cheap. `ExecuteOutcomeAsync` just hands you the same struct instead of unwrapping it.

When an exception does surface from `ExecuteAsync`/`Execute`, the original stack trace is preserved (`ExceptionDispatchInfo`) — it's thrown once, at the boundary.
