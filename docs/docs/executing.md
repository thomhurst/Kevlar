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

Multi-attempt [hedging](strategies/hedging.md) and asynchronous strategy callbacks require async execution.

`Task` and `ValueTask` delegates both work — your existing `Task`-returning methods flow straight in, no wrapping:

<!-- doc-test-ignore: Method declaration uses an ellipsis for the application implementation. -->
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
- Empty pipelines and pass-through boundary adapters preserve cancellation, results, and
  exceptions. Active strategies may deliberately transform them: timeout and hedging use linked
  delegate tokens, and timeout can surface `TimeoutExceededException`.
- When no strategy transforms an exception, throwing execution preserves its original instance
  and stack; `ExecuteOutcomeAsync` captures that same exception instead.

:::tip Always use the token you're handed
Your delegate receives a `CancellationToken` that combines your outer token with shield-driven cancellation (timeouts, hedging losers). Pass it to everything you await.
:::

## Ambient context

Kevlar invokes the first user delegate inline when no preceding strategy defers execution, so it initially sees the caller's current `SynchronizationContext`. A queued limiter execution and other deferred work may first invoke the delegate with no `SynchronizationContext`. Internal asynchronous continuations use `ConfigureAwait(false)` and do not marshal back to the caller's context; a later retry, fallback, timeout callback, or hedge may likewise run with no `SynchronizationContext`. Your own delegate controls whether its own awaits capture a context.

When every attempt must enter a UI context, capture its scheduler on the UI thread before calling
Kevlar, then schedule the delegate explicitly:

<!-- doc-test-ignore: viewModel represents the application's UI-bound implementation. -->
```csharp
var uiScheduler = TaskScheduler.FromCurrentSynchronizationContext();

await shield.ExecuteAsync(
    ct => Task.Factory.StartNew(
            async () => await viewModel.RefreshAsync(ct),
            ct,
            TaskCreationOptions.DenyChildAttach,
            uiScheduler)
        .Unwrap(),
    cancellationToken);
```

Kevlar invokes this wrapper again for every retry or hedge, so each attempt is scheduled onto the
captured context. Capture the scheduler only while running on the intended UI context. Strategy
hooks return `ValueTask`; make the hook itself `async` and await the scheduled `Task`:

<!-- doc-test-ignore: viewModel represents the application's UI-bound implementation. -->
```csharp
var uiRetryShield = Shield.Retry(options =>
{
    options.OnRetry = async retry =>
    {
        await Task.Factory.StartNew(
                async () => await viewModel.ShowRetryAsync(retry.AttemptNumber),
                retry.Context.CancellationToken,
                TaskCreationOptions.DenyChildAttach,
                uiScheduler)
            .Unwrap();
    };
});
```

Do not synchronously block while waiting for the shield: a single-threaded context must remain
free to run the scheduled work.
Hedged delegates are serialized by a single UI thread, and cancellation can prevent work that is
still queued from starting. The `async` scheduled lambda accepts either a `Task`- or
`ValueTask`-returning UI method; result-bearing variants preserve `TResult` through the unwrapped
`Task<TResult>`.

`ExecutionContext` still flows normally. `AsyncLocal<T>` values visible to the caller flow into actions and strategy callbacks, while parallel hedge attempts receive isolated logical snapshots so one attempt's mutations do not leak into another or a later execution. Calling Kevlar from work started under `ExecutionContext.SuppressFlow()` keeps that flow suppressed.

Synchronous `Execute` never pumps a `SynchronizationContext`. Retry delays and limiter queues block
the calling thread until they complete, so prefer `ExecuteAsync` for delayed or queued work.

## Synchronous execution compatibility

Every strategy hook returns `ValueTask`, and Kevlar never blocks the calling thread on one. Under
synchronous `Execute`, `ExecuteOutcome`, and `ExecuteWithContext`, a hook that completes
synchronously (`return default;`, `new(value)`, `ValueTask.CompletedTask`) runs inline at no extra
cost. A hook that returns an incomplete `ValueTask` fails that execution with
`NotSupportedException` — for example
`Synchronous execution does not support RetryOptions.OnRetry completing asynchronously on shield 'catalog'. Use ExecuteAsync instead of Execute, or make the callback complete synchronously.`
`ExecuteOutcome` returns that exception as a failed outcome. A few configurations are still
rejected statically, before the action runs:

| Pipeline configuration | Synchronous `Execute` behavior |
|---|---|
| Empty shield, fixed timeout, constant fallback, or hooks and fallback delegates that complete synchronously | Runs on the calling thread |
| Retry delay, queued rate limit, or queued concurrency limit | Blocks the calling thread until the delay or queue admission completes |
| `TimeoutGenerator`, `BreakDurationGenerator`, or `DelayGenerator` returning a completed `ValueTask` | Invokes the generator inline; no async transition is introduced |
| Any hook or generator that returns an incomplete `ValueTask`, including `ChaosBehaviorOptions.Behavior` | Throws `NotSupportedException` at that call; use `ExecuteAsync` or make the hook complete synchronously |
| A fallback recovery delegate that returns an incomplete `ValueTask` | Throws `NotSupportedException` at that call; use `ExecuteAsync` or make the delegate complete synchronously |
| `CircuitBreakerMonitor.Isolate()` / `Reset()` with an `OnStateChanged` hook that yields | Blocks until the observer completes; the observer runs on the thread pool |
| Multi-attempt hedging | Throws `NotSupportedException` before the action runs |
| Any `UseRateLimiter` adapter | Throws `NotSupportedException` before the action runs; use `ExecuteAsync` |
| Custom strategy returning an incomplete `ValueTask` | Blocks at the execution boundary; custom code must avoid capturing a single-threaded `SynchronizationContext` |

[`KEV012`](analyzers.md#kev012-async-configuration-with-synchronous-execute) reports `async`
delegates assigned to hooks or fallback recovery on a shield that is passed to `Execute`. A shield
obtained from a field, parameter, or opaque factory may still contain such a delegate, so the
runtime guard remains authoritative.

## Zero-closure hot paths

Capturing locals in a lambda allocates a closure on every call. On hot paths, thread your state through instead:

```csharp
await shield.ExecuteAsync(
    (client, id),                                     // your state, as a tuple
    static (s, ct) => s.client.GetUserAsync(s.id, ct), // static lambda: nothing captured
    cancellationToken);
```

The `static` keyword makes the compiler enforce it: this call site allocates nothing for the delegate.

## Execution-scoped metadata and context

Use `ExecuteWithContextAsync` when metadata must be available before the outermost strategy runs,
or when the action needs the current execution context:

```csharp
var requestIdKey = new KevlarKey<string>("request-id");
var user = await shield.ExecuteWithContextAsync(
    (client, id, requestId: "req-42", requestIdKey),
    static (state, properties) =>
        properties.Set(state.requestIdKey, state.requestId),
    static (state, context) =>
    {
        if (context.Properties.Contains(state.requestIdKey))
        {
            context.Properties.Remove(state.requestIdKey);
        }

        return state.client.GetUserAsync(state.id, context.CancellationToken);
    },
    cancellationToken);
```

The initializer runs once, after the caller cancellation check and before any strategy. Every
strategy and retry attempt sees the same logical properties. Hedged attempts copy the initialized
properties when each fork launches; later mutations stay isolated between attempts. The action's
`context.CancellationToken` is the effective token for that attempt, including timeout and hedge
cancellation.

Use `Contains` and `Remove` when metadata is optional or should not flow to later strategies.
`Count` reports the entries in the current execution. `KevlarProperties` is not thread-safe; each
parallel hedge attempt receives its own snapshot, so mutations remain local to that attempt.

When you only need to *read* the context — the shield name, the effective token, the ambient
`TimeProvider` — there is a shorter overload that skips the state and initializer entirely:

```csharp
var name = await shield.ExecuteWithContextAsync(
    static context => new ValueTask<string?>(context.ShieldName),
    cancellationToken);
```

It exists in the same shapes as the full form: result-returning and void, synchronous and
asynchronous, `ValueTask` and `Task`, on both `Shield` and `Shield<TResult>`. It delegates to the
state-based overload, passing your delegate as the state, so it stays closure-free when the lambda
is `static`.

Inside a context-aware action, pass its context to another shield to preserve the logical execution
across the nested pipeline:

```csharp
var nestedResult = await Shield.Empty.ExecuteWithContextAsync(async parentContext =>
{
    var asyncResult = await Shield.Retry(1, Backoff.None).ExecuteWithContextAsync(
        parentContext,
        static childContext => new ValueTask<int>(childContext.ShieldName?.Length ?? 0));
    var syncResult = Shield.Empty.ExecuteWithContext(
        parentContext,
        static childContext => childContext.ShieldName?.Length ?? 0);
    return asyncResult + syncResult;
});
```

The child inherits the parent's properties, effective cancellation token, and `TimeProvider`.
Child property changes merge back when the nested call completes. Await an asynchronous child
before the parent action exits; both contexts are pooled and must not be retained.

`ExecuteWithContext` provides the same contract for synchronous actions. Both `Shield` and
`Shield<TResult>` support the context-aware shape; `Task` and `ValueTask` actions are accepted.
Use static initializer and action delegates with the state parameter to avoid closures. The pooled
context path itself allocates 0 B/op after warm-up when existing property storage can be reused;
adding a new key allocates its reusable typed property slot, while value types are not boxed.

Keep these three kinds of state distinct:

- `TState` is caller-owned action input and remains the simplest, fastest path for business data.
- `KevlarContext.Properties` is mutable metadata for one logical execution and its strategy callbacks.
- Breaker and limiter state belongs to the shared shield/strategy instance, not one execution.

`KevlarContext` and its `Properties` bag are pooled. Read or mutate them only during the current
action or strategy callback. Never retain either object, return it to the caller, or use it after
the callback finishes. Kevlar clears properties before a pooled context serves another execution.

### Reading properties after execution

Add an `onCompleted` callback when the caller needs metadata that the action or a strategy wrote.
Kevlar invokes it after the final pipeline outcome, on success or failure, and before returning the
context to the pool:

```csharp
var attemptsKey = new KevlarKey<int>("attempts");
var attempts = 0;

await shield.ExecuteWithContextAsync(
    (attemptsKey, onAttempts: (Action<int>)(value => attempts = value)),
    static (_, _) => { },
    static (state, context) => FetchAsync(context.CancellationToken),
    static (state, properties) =>
        state.onAttempts(properties.GetOrDefault(state.attemptsKey)),
    cancellationToken);
```

The callback receives `KevlarProperties`, not the pooled `KevlarContext`, so it cannot keep the
execution alive accidentally. Copy any values you need into caller-owned state during the callback.
Exceptions thrown by `onCompleted` are ignored and never replace the execution result or exception.
For hedging, the callback sees properties from the winning attempt.

## Outcomes without exceptions

When a failure is an expected outcome rather than an exceptional one, skip the throw/catch entirely and inspect the outcome:

```csharp
Outcome<User> outcome = await shield.ExecuteOutcomeAsync(ct => LoadAsync(ct));

if (outcome.TryGetResult(out var user))
{
    return user;
}

logger.LogError(outcome.Exception, "gave up loading user");
return cached;
```

`TryGetResult` returns `true` exactly when the outcome succeeded. Its nullability annotation
also tells flow analysis that a non-nullable result is available inside the success branch.
Use `GetResultOrRethrow()` when you want the throwing path instead.

Void and synchronous work use the same pattern through non-generic `Outcome` and
`ExecuteOutcome`:

```csharp
Outcome saved = await shield.ExecuteOutcomeAsync(ct => SaveAsync(ct), cancellationToken);
Outcome<int> computed = shield.ExecuteOutcome(ct => ComputeSync(ct), cancellationToken);

if (!saved.IsSuccess)
{
    saved.Rethrow();
}
```

`Outcome<T>` converts implicitly to `Outcome`, preserving success or the captured exception.

No-throw execution also supports state-passing `ValueTask` and `Task` delegates. Use a static
delegate to keep caller data out of a closure:

```csharp
Outcome<User> outcome = await shield.ExecuteOutcomeAsync(
    (client, id),
    static (s, ct) => s.client.GetUserAsync(s.id, ct),
    cancellationToken);
```

Retry attempts receive the same caller state. Hedged attempts also receive that state concurrently,
so mutable state used by a hedged action must be thread-safe.

This is also how failures travel *between* strategies internally — as `Outcome<T>` structs, not thrown exceptions — which is a big part of why the pipeline is cheap. `ExecuteOutcomeAsync` just hands you the same struct instead of unwrapping it.

When an exception does surface from `ExecuteAsync`/`Execute`, the original stack trace is preserved (`ExceptionDispatchInfo`) — it's thrown once, at the boundary.
