---
sidebar_position: 12
---

# Analyzers

Install the optional analyzer package in each project that builds Kevlar pipelines:

```bash
dotnet add package Kevlar.Analyzers
```

The analyzers report only hazards that are provable from the current expression or a stable local
initializer. They do not guess what a factory method returns or follow a local that is reassigned.
Generated code is ignored.

| Rule | Severity | Hazard |
|---|---|---|
| `KEV001` | Warning | execution delegate ignores its `CancellationToken` |
| `KEV002` | Warning | known multi-attempt hedging pipeline is passed to synchronous `Execute` |
| `KEV003` | Warning | inner fallback makes retry, hedging, or circuit breaker unreachable |

## KEV001: ignored execution cancellation

Kevlar cancels the token passed to an execution delegate when the caller cancels, a timeout expires,
or a hedge loses. Work that ignores the token can continue after the shield has returned.

```csharp
await shield.ExecuteAsync(ct => client.GetAsync(url));        // KEV001
await shield.ExecuteAsync(ct => client.GetAsync(url, ct));    // clean
```

Pass the token to cancellable work. Name it `_` only when the operation is truly synchronous or
uncancellable and ignoring cancellation is deliberate.

## KEV002: synchronous hedging

Multi-attempt hedging races concurrent attempts and therefore supports only asynchronous execution.
Synchronous `Execute` throws for a shield containing a hedge with a known `MaxAttempts` greater than
one, so use `ExecuteAsync` or remove hedging:

```csharp
var hedged = Shield.Hedge(2, TimeSpan.FromMilliseconds(50));

var value = await hedged.ExecuteAsync(ct => LoadAsync(ct));   // clean
```

The rule recognizes direct fluent chains, composed shields, and stable local aliases, including typed
shields and extension-method syntax. It deliberately skips opaque factory results, reassigned locals,
and hedging whose configured attempt count is not a compile-time constant.

## KEV003: unreachable reactive strategy

A fallback placed inside retry, hedging, or circuit breaker under the same handling clause consumes
every handled failure before the outer strategy sees it. Kevlar rejects that pipeline at construction
time. Put fallback first so it wraps the reactive strategy:

```csharp
var shield = Shield.For<int>()
    .Fallback(-1)
    .Retry(3);
```

Alternatively, start a new, narrower handling clause before the inner fallback when layered recovery
is intentional:

```csharp
var shield = Shield.For<int>()
    .Retry(3)
    .When<TimeoutExceededException>()
    .Fallback(-1);
```

No automatic code fix is supplied for `KEV002` or `KEV003`: changing sync control flow or strategy
order can change application semantics.

## Suppression

Suppress one reviewed site with ordinary C# warning pragmas and record why the analyzer cannot see
the wider invariant:

```csharp
#pragma warning disable KEV002 // Execution is unreachable in the synchronous deployment mode.
var value = shield.Execute(_ => 1);
#pragma warning restore KEV002
```

To suppress a rule project-wide, append its ID to `NoWarn`:

```xml
<NoWarn>$(NoWarn);KEV002</NoWarn>
```

Prefer a narrow pragma. Project-wide suppression can hide newly introduced unsafe call sites.
