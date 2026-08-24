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
| `KEV004` | Warning | stateful shield or partition provider is constructed for one execution |
| `KEV005` | Warning | void fallback is executed with a result-returning delegate |
| `KEV006` | Warning | hedging is added to an untyped shield, whose action must be idempotent |
| `KEV007` | Warning | handling clause never reaches a reactive strategy |
| `KEV008` | Warning | fluent chaining result is discarded as a statement |
| `KEV009` | Info | strategy inherits a handling clause declared earlier in the chain |
| `KEV010` | Info | default-result clause is written for a value type, whose default is usually valid |

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
var hedged = Shield.Hedge(2, delay: TimeSpan.FromMilliseconds(50));

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

## KEV004: per-execution stateful shields

Circuit breakers, rate limiters, concurrency limiters, and partition providers retain state between
executions. Constructing one immediately before `Execute`, `ExecuteAsync`, or
`ExecuteOutcomeAsync` discards that state after one call, so a circuit never accumulates failures
and limiter capacity is not shared:

```csharp
// KEV004: a new circuit exists for only this call.
await Shield.CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30))
    .ExecuteAsync(static _ => ValueTask.CompletedTask);
```

<!-- doc-test-declaration: split-before=await _dependencyShield -->
```csharp
// Clean: every call shares the same circuit.
private static readonly Shield _dependencyShield =
    Shield.CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));

await _dependencyShield.ExecuteAsync(static _ => ValueTask.CompletedTask);
```

Store stateful shields in a field, singleton or keyed dependency-injection registration, or registry.
Store `PartitionedShield<TKey>` and `PartitionedShield<TKey, TResult>` providers for the same reason:
their retained per-key shields disappear when the provider is constructed per call.

The rule is deliberately conservative. It reports inline construction and a stable local or local
alias that has exactly one use in the same method or lambda. Fields, parameters, opaque factory
results, locals with multiple uses, locals captured by nested lambdas, stateless-only pipelines,
generated code, and assemblies ending in `.Test` or `.Tests` remain clean. Custom `Strategy`
instances are not assumed to be stateful because that cannot be proven from their public contract.
Test methods marked with standard TUnit, xUnit, NUnit, or MSTest attributes are also ignored.

## KEV005: void fallback with a result

A fallback on non-generic `Shield` can recover void executions only. If that shield executes a
result-returning delegate, the fallback cannot produce the required value and fails at runtime when
it handles an outcome:

```csharp
var voidShield = Shield.Retry(3)
    .Fallback(static _ => ValueTask.CompletedTask);

var value = await voidShield.ExecuteAsync(static _ => new ValueTask<int>(42)); // KEV005
```

Build a result-aware shield and use its typed fallback overload instead:

```csharp
var resultShield = Shield.For<int>()
    .Retry(3)
    .Fallback(-1);

var value = await resultShield.ExecuteAsync(static _ => new ValueTask<int>(42));
```

The rule recognizes same-expression chains and stable local aliases for synchronous, asynchronous,
outcome-returning, context-aware, and `Task<T>` execution overloads. It deliberately skips
parameters, returned shields, reassigned locals, and fields because their construction cannot be
proved by the current intraprocedural analysis.

## KEV006: hedging on an untyped shield

Hedging launches the execution delegate more than once, concurrently. An untyped `Shield` can judge
those attempts only by their exceptions, so every attempt it starts runs to completion against the
real dependency — and a losing hedge has still done its work. Duplicate writes, charges, or sends are
observable unless the action is idempotent:

```csharp
var shield = Shield.Hedge(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(50)); // KEV006
```

Build a result-aware shield so a [result clause](handling-failures.md#result-clauses) can decide
which attempt is acceptable:

```csharp
var shield = Shield.For<HttpResponseMessage>()
    .WhenResult(response => (int)response.StatusCode >= 500)
    .Hedge(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(50));
```

If the untyped shape is deliberate — the action is a genuinely idempotent read — suppress the
warning at that site with a pragma that records why.

The rule reports every `Hedge` overload that returns the untyped `Shield`: the static
`Shield.Hedge(...)` factories, the `ShieldExtensions.Hedge(...)` chaining methods, and
`ShieldBuilder.Hedge(...)`. `Shield<T>.Hedge(...)` and `ShieldBuilder<T>.Hedge(...)` are never
flagged.

## KEV007: dead handling clause

A [handling clause](handling-failures.md) changes nothing on its own — it only takes effect once a
reactive strategy (retry, circuit breaker, hedging, fallback, or a `Use` factory) consumes it. Two
shapes silently do nothing.

The first is a clause whose `ShieldBuilder` is dropped instead of being finished with a strategy.
Builders are immutable — every `When…`/`Or…` returns a *new* builder and leaves its receiver
untouched — so a dropped builder is simply lost, including a dropped `Or…` that looks like it is
extending a clause in place:

<!-- doc-test-ignore: Deliberately dead clauses that the analyzer is expected to flag. -->
```csharp
Shield.When<HttpRequestException>();                          // KEV007 — nothing consumes it
var clause = Shield.When<HttpRequestException>().Or<TimeoutExceededException>();  // KEV007 — never used

var transient = Shield.When<HttpRequestException>();
transient.Or<TimeoutExceededException>();                     // KEV007 — the new builder is dropped
var shield = transient.Retry(3);                              // still HttpRequestException only
```

The second is a clause that a later `When…` or `WhenAnyError()` replaces while only *proactive*
strategies — timeout, rate limit, concurrency limit — sat in between. Proactive strategies never
consult clauses, so the first clause never applied to anything:

<!-- doc-test-ignore: Deliberately dead clauses that the analyzer is expected to flag. -->
```csharp
var shield = Shield
    .When<HttpRequestException>()                 // KEV007 — only the timeout saw this clause
    .Timeout(TimeSpan.FromSeconds(5))
    .WhenAnyError()
    .Retry(3);
```

Fix either by finishing the clause with the reactive strategy it was written for:

```csharp
var shield = Shield
    .When<HttpRequestException>()
    .Timeout(TimeSpan.FromSeconds(5))
    .Retry(3)                                     // the clause reaches a reactive strategy
    .WhenAnyError()
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
```

The rule follows one fluent chain and a clause builder stored in a local. It deliberately stays
quiet when the builder escapes — returned, passed as an argument, assigned to a field — and at
`Wrap`/`Compose` boundaries, because a clause's fate is no longer visible there.

## KEV008: discarded fluent chaining result

Shields — and the clause builders they hand back — are immutable. Every fluent method returns a
*new* value and leaves its receiver untouched, so a chaining call written as a statement configures
nothing:

<!-- doc-test-ignore: A deliberately discarded chain that the analyzer is expected to flag. -->
```csharp
var shield = Shield.Timeout(TimeSpan.FromSeconds(5));
shield.Retry(3);                                   // KEV008 — the retry is thrown away
await shield.ExecuteAsync(ct => CallAsync(ct));    // still just a timeout
```

Keep the returned shield instead:

```csharp
var shield = Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3);
```

The rule fires only on a statement whose value is a `Shield` or `Shield<TResult>`, so assigning the
result, returning it, or passing it as an argument is never flagged. Discarded *clause builders* are
reported by [`KEV007`](#kev007-dead-handling-clause) instead, which names that hazard directly.

## KEV009: inherited handling clause

A [handling clause](handling-failures.md) stays ambient: it applies to the strategy it is attached to
*and* to every reactive strategy chained after it, until a new `When…` replaces it, `WhenAnyError()`
resets it, or `Wrap`/`Compose` seals it. That is by design — writing the clause once is the point —
but only the *first* strategy states it at its own call site. `KEV009` is an informational hint, not
a warning: it marks the second and later strategies so the clause's span is visible in the editor.

```csharp
var shield = Shield
    .When<HttpRequestException>()
    .Retry(3)                                      // states the clause here
    .Timeout(TimeSpan.FromSeconds(5))
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
//   ^^^^^^^^^^^^^^ KEV009 — only HttpRequestException opens this circuit
```

Nothing needs fixing if that is what you meant. If it is not, give the later strategy its own
handling — a new clause, a reset, or a local override:

```csharp
var shield = Shield
    .When<HttpRequestException>()
    .Retry(3)
    .WhenAnyError()
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
```

Proactive strategies — timeout, rate limit, concurrency limit — never consult a clause, so they are
never flagged and never end the inherited span. A strategy that sets `HandlesException` or
`HandlesResult` in its own options has opted out of the ambient clause and is not flagged either.
The rule follows one fluent chain and a stable local, and stops at `Wrap`/`Compose` boundaries.

Because it is `Info`, `KEV009` never fails a build, including under `TreatWarningsAsErrors`. Turn it
off entirely with `dotnet_diagnostic.KEV009.severity = none` in `.editorconfig` if the ambient model
is already second nature to your team.

## KEV010: default-result clause on a value type

`WhenResultIsDefault` and `OrResultIsDefault` were named for reference types, where `default(T)` is
`null` and a missing value is usually the failure. On a value type the same clause treats `0`,
`false`, or an empty struct as a failure worth retrying, hedging, or falling back from — which is
occasionally right and often a foot-gun:

```csharp
var shield = Shield.For<int>().WhenResultIsDefault().Retry(3);
//                             ^^^^^^^^^^^^^^^^^^^ KEV010 — is 0 really a failure here?
```

Say which results are failures instead, or keep the clause if `0` genuinely is one:

```csharp
var shield = Shield.For<int>().WhenResult(static count => count < 0).Retry(3);
```

Reference-type shields have a clause that cannot be written by mistake — `WhenResultIsNull` and
`OrResultIsNull` are constrained to reference types, so they never compile for an `int`:

```csharp
var shield = Shield.For<string>().WhenResultIsNull().Retry(3);
```

The rule reads `TResult` from the shield being configured. `Nullable<T>` results are left alone —
their default *is* the missing value — and so is generic code, where `default(TResult)` is the only
term available. Like `KEV009`, `KEV010` is `Info` and never fails a build; silence it per site with
a pragma or project-wide with `dotnet_diagnostic.KEV010.severity = none`.

## Suppression

Suppress one reviewed site with ordinary C# warning pragmas and record why the analyzer cannot see
the wider invariant:

```csharp
#pragma warning disable KEV004 // This isolated circuit is intentional in a one-shot diagnostic.
var value = Shield.CircuitBreaker(consecutiveFailures: 1, breakDuration: TimeSpan.FromSeconds(1)).Execute(_ => 1);
#pragma warning restore KEV004
```

To suppress a rule project-wide, append its ID to `NoWarn`:

```xml
<NoWarn>$(NoWarn);KEV004</NoWarn>
```

Prefer a narrow pragma. Project-wide suppression can hide newly introduced unsafe call sites.
