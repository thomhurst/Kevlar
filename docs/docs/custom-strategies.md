---
sidebar_position: 9
---

# Custom Strategies

Everything in Kevlar is a `Strategy` — middleware over an `Outcome<T>` pipeline. Retry, circuit breaker, timeout: all of them are implemented on the same surface you extend.

## A logging strategy

<!-- doc-test-declaration: split-before=var shield -->
```csharp
public sealed class LoggingStrategy(ILogger logger) : Strategy
{
    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
        Continuation<T, TState> next, KevlarContext context)
    {
        var start = context.TimeProvider.GetTimestamp();
        var outcome = await next.InvokeAsync(context);
        logger.LogInformation("{Shield} took {Elapsed}", context.ShieldName,
            context.TimeProvider.GetElapsedTime(start));
        return outcome;
    }
}

var shield = Shield.Use(new LoggingStrategy(logger)).Retry(3);
```

`Use` slots your strategy into the chain at that position — here, outside the retries, so it logs total elapsed time across all attempts. Put it after `Retry` to log each attempt instead.

Override `Describe()` so `shield.ToString()` names your strategy meaningfully in [pipeline descriptions](observability.md#pipeline-descriptions):

<!-- doc-test-strategy-member -->
```csharp
public override string Describe() => "Logging";
```

## The contract

Your strategy receives:

- **`next`** — the rest of the pipeline (inner strategies, then the user's delegate). Invoke it with `next.InvokeAsync(context)`.
- **`context`** — the [`KevlarContext`](#kevlarcontext) for this execution.

And returns an `Outcome<T>`: success-with-result or failure-with-exception, as a struct.

`next.InvokeAsync(context)` preserves the caller-supplied state and the same context, including
its current name, time provider, cancellation token, and properties. Synchronous throws and
asynchronous faults from inner strategies or the user's delegate are normalized to failure
outcomes, so a valid continuation does not throw. A default, uninitialized
`Continuation<T, TState>` returns an `InvalidOperationException` outcome.

The power is in how many times you call `next`:

| Calls to `next` | You've built a | Examples in the box |
|---|---|---|
| zero | short-circuit | circuit breaker (open), rate limit, concurrency limit rejection |
| one | decorator | timeout, fallback, logging, metrics |
| many | repeater | retry, hedging |

## Failures are outcomes, not throws

Strategies return failures as `Outcome<T>` values rather than throwing, so outer strategies can react to them cheaply:

<!-- doc-test-strategy-member -->
```csharp
public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
    Continuation<T, TState> next, KevlarContext context)
{
    var outcome = await next.InvokeAsync(context);

    if (!outcome.IsSuccess)
    {
        // inspect outcome.Exception, decide what to do:
        // return outcome unchanged, replace it, or try next again
    }

    return outcome;
}
```

The exception is only thrown once — at the pipeline boundary, back in the caller's frame, with its original stack trace intact.

## Context properties

`KevlarContext.Properties` is isolated per execution. A `KevlarKey<T>` is identified by both
its case-sensitive name and `T`: keys with the same name and different value types do not
collide, while new key instances with the same name and type address the same value. Empty names
are valid. Stored `null` is present and distinct from a missing key, so `TryGet` returns `true`
with a `null` value and `GetOrDefault` does not substitute its fallback.

Callers can seed this bag with `ExecuteWithContextAsync` or `ExecuteWithContext`. The initializer
runs before the outermost strategy, retries reuse the logical context, and hedged attempts receive
detached property snapshots. See [Executing](executing.md#execution-scoped-metadata-and-context).

## KevlarContext

The context flows through the whole pipeline:

- `context.ShieldName` — set via `WithName`, for logs and metrics.
- `context.TimeProvider` — **always use this instead of `DateTime`/`Stopwatch`/`Task.Delay`**, so your strategy stays [testable with `FakeTimeProvider`](testing.md) like the built-ins.
- `context.CancellationToken` — the current token. Strategies such as timeouts *replace* this for the layers beneath them — which is why delegates must use the token they're handed rather than a captured one.
- `context.IsSynchronous` — `true` under `Execute`; branch on it if your strategy would otherwise block or break a sync caller (hedging throws for sync callers this way).
- `context.Properties` — a typed property bag: `Set(key, value)`, `TryGet(key, out value)`, `GetOrDefault(key)`, keyed by `KevlarKey<T>`:

<!-- doc-test-declaration: split-before=context.Properties -->
```csharp
static readonly KevlarKey<string> TenantId = new("tenant-id");

context.Properties.Set(TenantId, "acme");
if (context.Properties.TryGet(TenantId, out var tenant)) { /* ... */ }
```

Contexts are pooled and recycled by the engine — never store one beyond the execution.
The continuation also belongs to that execution; invoke it only while `ExecuteAsync` is running.

## Thread safety

One strategy instance is shared by every execution of the shield containing it — and by every shield it's composed into. That's the [state-sharing rule](composition.md#the-state-sharing-rule) working in your favour, but it means your strategy must be thread-safe, like the built-in breakers and limiters.

Stateless strategies can be shared directly. Stateful strategies must synchronize their own
mutable fields. Per-execution data belongs in local variables or `KevlarContext.Properties`, not
in strategy instance fields.
