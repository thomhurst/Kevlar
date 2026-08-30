---
sidebar_position: 13
---

# Analyzers

The `Kevlar` package includes these analyzers automatically. No separate analyzer package is
required. They report diagnostics only; Kevlar does not install code fixes or IDE code actions.

## Toolchain compatibility

Kevlar's analyzers reference Microsoft.CodeAnalysis 4.8.0. Use Visual Studio 2022 17.8 or later,
or the .NET 8.0.100 SDK or later to run them. The package places them in its `roslyn4.8` analyzer
band, so older compiler hosts skip them instead of reporting `CS9057`; the Kevlar runtime library
remains available. Update the compiler host when those projects should also run Kevlar's analyzers.

## Keep analyzers enabled

Treat bundled analyzers as part of Kevlar's safety contract. Do not exclude analyzer assets or
lower diagnostic severities; fix each reported hazard or make safe intent explicit in code.

The analyzers report only hazards that are provable from the current expression or a stable local
initializer. They do not guess what a factory method returns or follow a local that is reassigned.
Generated code is ignored.

## Enabling stricter configuration hints

`KEV009`, `KEV010`, and `KEV011` are opt-in informational design hints. Enable whichever checks
match your team's conventions in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.KEV009.severity = suggestion
dotnet_diagnostic.KEV010.severity = suggestion
dotnet_diagnostic.KEV011.severity = suggestion
```

The warning-level safety rules remain enabled by default.

| Rule | Severity | Default | Hazard |
|---|---|---|---|
| `KEV001` | Warning | On | execution delegate ignores its `CancellationToken` |
| `KEV002` | Warning | On | known multi-attempt hedging pipeline is passed to synchronous `Execute` |
| `KEV003` | Warning | On | inner fallback makes retry, hedging, or circuit breaker unreachable |
| `KEV004` | Warning | On | stateful shield or partition provider is constructed for one execution |
| `KEV005` | Warning | On | void fallback is executed with a result-returning delegate |
| `KEV006` | Warning | On | hedging is added to an untyped shield, whose action must be idempotent |
| `KEV007` | Warning | On | handling clause never reaches a reactive strategy |
| `KEV008` | Warning | On | fluent chaining result is discarded as a statement |
| `KEV009` | Info | Off | strategy inherits a handling clause declared earlier in the chain |
| `KEV010` | Info | Off | default-result clause is written for a value type, whose default is usually valid |
| `KEV011` | Info | Off | reactive strategy relies on implicit default handling, which includes programming errors |
| `KEV012` | Warning | On | a delegate that completes asynchronously is assigned to a hook of a shield passed to synchronous `Execute` |
| `KEV014` | Warning | On | a pooled event context is captured by deferred work |

`KEV013` is intentionally unused. `KEV012` covers synchronous-execution callback hazards.

## KEV001: ignored execution cancellation

Kevlar cancels the token passed to an execution delegate when the caller cancels, a timeout expires,
or a hedge loses. Work that ignores the token can continue after the shield has returned.

<!-- doc-test-diagnostic: KEV001 -->
```csharp
await shield.ExecuteAsync(ct => client.GetAsync(url));        // KEV001
await shield.ExecuteAsync(ct => client.GetAsync(url, ct));    // clean
```

Pass the token to cancellable work. Name it `_` only when the operation is truly synchronous or
uncancellable and ignoring cancellation is deliberate.

## KEV002: synchronous hedging

Multi-attempt hedging races concurrent attempts and therefore supports only asynchronous execution.
Synchronous `Execute` throws for a shield containing a hedge with a known `MaxHedgedAttempts` greater than
zero, so use `ExecuteAsync` or remove hedging:

```csharp
var hedged = Shield.For<User>().Hedge(2, delay: TimeSpan.FromMilliseconds(50));

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
    .FallbackTo(-1)
    .Retry(3);
```

Alternatively, start a new, narrower handling clause before the inner fallback when layered recovery
is intentional:

```csharp
var shield = Shield.For<int>()
    .Retry(3)
    .When<TimeoutExceededException>()
    .FallbackTo(-1);
```

## KEV004: per-execution stateful shields

Circuit breakers, rate limiters, concurrency limiters, and partition providers retain state between
executions. Constructing one immediately before `Execute`, `ExecuteAsync`, or
`ExecuteOutcomeAsync` discards that state after one call, so a circuit never accumulates failures
and limiter capacity is not shared:

<!-- doc-test-diagnostic: KEV004 -->
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
Store [`PartitionedShield<TKey>`](partitioning.md) and
[`PartitionedShield<TKey, TResult>`](partitioning.md) providers for the same reason: their retained
per-key shields disappear when the provider is constructed per call.

The rule is deliberately conservative. It reports inline construction and a stable local or local
alias that has exactly one use in the same method or lambda. Fields, parameters, opaque factory
results, locals with multiple uses, locals captured by nested lambdas, stateless-only pipelines,
generated code, and assemblies ending in `.Test` or `.Tests` remain clean. Custom `Strategy`
instances are not assumed to be stateful because that cannot be proven from their public contract.
Test methods marked with standard TUnit, xUnit, NUnit, or MSTest attributes are also ignored.

## KEV005: void fallback with a result

A fallback on non-generic `Shield` can recover void executions only. A shield containing one rejects
every result-returning execution at the execution boundary, before the delegate or any strategy
runs—even when the delegate would have succeeded:

<!-- doc-test-diagnostic: KEV005 -->
```csharp
var shield = Shield.Fallback(static _ => ValueTask.CompletedTask);

var value = await shield.ExecuteAsync(static _ => new ValueTask<int>(42)); // KEV005
```

Build a result-aware shield and use its typed fallback overload instead:

```csharp
var shield = Shield.For<int>().FallbackTo(-1);

var value = await shield.ExecuteAsync(static _ => new ValueTask<int>(42));
```

The rule recognizes same-expression chains and stable local aliases for synchronous, asynchronous,
outcome-returning, context-aware, and `Task<TResult>` execution overloads. It deliberately skips
parameters, returned shields, reassigned locals, and fields because their construction cannot be
proved by the current intraprocedural analysis.

## KEV006: hedging on an untyped shield

Hedging launches the execution delegate more than once, concurrently. An untyped `Shield` can judge
those attempts only by their exceptions, so every attempt it starts runs to completion against the
real dependency — and a losing hedge has still done its work. Duplicate writes, charges, or sends are
observable unless the action is idempotent:

<!-- doc-test-diagnostic: KEV006 -->
```csharp
var shield = Shield.Hedge(maxHedgedAttempts: 2, delay: TimeSpan.FromMilliseconds(50)); // KEV006
```

Build a result-aware shield so a [result clause](handling-failures.md#result-clauses) can decide
which attempt is acceptable:

```csharp
var shield = Shield.For<HttpResponseMessage>()
    .WhenResult(response => (int)response.StatusCode >= 500)
    .Hedge(maxHedgedAttempts: 2, delay: TimeSpan.FromMilliseconds(50));
```

Use a typed shield even for genuinely idempotent reads. This keeps result selection explicit and
avoids normalizing an unsafe shape through suppression.

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

<!-- doc-test-diagnostic: KEV007*3 -->
```csharp
Shield.When<HttpRequestException>();                          // KEV007 — nothing consumes it
var clause = Shield.When<HttpRequestException>().Or<TimeoutExceededException>();  // KEV007 — never used

var transient = Shield.When<HttpRequestException>();
transient.Or<TimeoutExceededException>();                     // KEV007 — the new builder is dropped
var shield = transient.Retry(3);                              // still HttpRequestException only
```

The second is a clause that a later `When…` or `WithDefaultHandling()` replaces while only *proactive*
strategies — timeout, rate limit, concurrency limit — sat in between. Proactive strategies never
consult clauses, so the first clause never applied to anything:

<!-- doc-test-diagnostic: KEV007 -->
```csharp
var shield = Shield
    .When<HttpRequestException>()                 // KEV007 — only the timeout saw this clause
    .Timeout(TimeSpan.FromSeconds(5))
    .WithDefaultHandling()
    .Retry(3);
```

Fix either by finishing the clause with the reactive strategy it was written for:

```csharp
var shield = Shield
    .When<HttpRequestException>()
    .Timeout(TimeSpan.FromSeconds(5))
    .Retry(3)                                     // the clause reaches a reactive strategy
    .WithDefaultHandling()
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
```

The rule follows one fluent chain and a clause builder stored in a local. It deliberately stays
quiet when the builder escapes — returned, passed as an argument, assigned to a field — and at
`Wrap`/`Compose` boundaries, because a clause's fate is no longer visible there.

## KEV008: discarded fluent chaining result

Shields — and the clause builders they hand back — are immutable. Every fluent method returns a
*new* value and leaves its receiver untouched, so a chaining call written as a statement configures
nothing:

<!-- doc-test-diagnostic: KEV008 -->
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
*and* to every reactive strategy chained after it, until a new `When…` replaces it, `WithDefaultHandling()`
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
    .WithDefaultHandling()
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
```

Proactive strategies — timeout, rate limit, concurrency limit — never consult a clause, so they are
never flagged and never end the inherited span. A strategy that sets `HandlesException` or
`HandlesResult` in its own options has opted out of the ambient clause and is not flagged either.
The rule follows one fluent chain and a stable local, and stops at `Wrap`/`Compose` boundaries.

`KEV009` is disabled by default. Enable it through the
[stricter configuration hints](#enabling-stricter-configuration-hints) when the extra editor signal
fits your team's conventions.

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
term available. `KEV010` is disabled by default and can be enabled through the
[stricter configuration hints](#enabling-stricter-configuration-hints).

## KEV011: implicit default handling

A retry, circuit breaker, hedge, or fallback without an explicit clause uses its
[default handling](handling-failures.md#the-default). Retry, circuit breaker, and hedge exclude
cancellation, fail-fast rejections, and fatal runtime failures; fallback additionally handles
fail-fast rejections. Every strategy default still includes programming errors such as
`ArgumentException`, `InvalidOperationException`, and `NullReferenceException`:

```csharp
var shield = Shield.Retry(3);
//                  ^^^^^ KEV011 — programming errors are retried too
```

Declare the failures the strategy expects when it is intended for transient faults:

```csharp
var shield = Shield
    .When<HttpRequestException>()
    .Or<TimeoutExceededException>()
    .Retry(3);
```

A local `HandlesException` or `HandlesResult` override also makes the policy explicit. The rule
does not report proactive strategies, an explicit `WithDefaultHandling()` reset, or opaque configuration
that the analyzer cannot prove still uses the default.

`KEV011` is an opt-in informational design hint, not a warning. After enabling it, make deliberate
default handling explicit with `WithDefaultHandling()` so future changes remain warning-clean:

```csharp
var shield = Shield.Empty
    .WithDefaultHandling()
    .Retry(3);
```

## KEV012: async configuration with synchronous Execute

Every strategy hook and fallback recovery delegate returns `ValueTask`, and Kevlar never blocks the
calling thread on one: under synchronous `Execute`, a delegate that does not complete synchronously
throws `NotSupportedException` at that call. `KEV012` reports the statically visible form — an
`async` lambda, an `async` anonymous method, or a method group naming an `async` method assigned to
a hook or fallback recovery on a shield that is then passed to `Execute`:

<!-- doc-test-diagnostic: KEV012 -->
```csharp
var shield = Shield.Retry(options =>
    options.OnRetry = async _ => await Task.Yield());

var value = shield.Execute(static _ => 42); // KEV012 and NotSupportedException at runtime
```

Use `ExecuteAsync`, or make the delegate complete synchronously. A non-`async` lambda that returns
`default`, `ValueTask.CompletedTask`, or `new(...)` is not reported and runs through `Execute`:

```csharp
var shield = Shield.Timeout(options =>
    options.TimeoutGenerator = static timeout => new(
        timeout.Context.ShieldName == "interactive"
            ? TimeSpan.FromSeconds(2)
            : timeout.Timeout));

var value = shield.Execute(static _ => 42);
```

The rule recognizes hook assignments in a configuration lambda on the same fluent chain or through
a stable local alias. It also recognizes `Fallback` recovery delegates that are statically `async`.
`UseRateLimiter` adapters remain async-only. Configuration lambdas are inspected only on known
Kevlar strategy factories, so a custom extension that merely inspects genuine Kevlar options
remains clean.

The analyzer does not guess through fields, parameters, local delegates, or opaque factories; the
runtime guard still protects those cases. `ExecuteAsync` and configurations whose hooks complete
synchronously remain clean.

## KEV014: deferred event-context capture

Scheduling work from a callback can outlive the pooled event context. Copy required values first:

```csharp
var shield = Shield.Retry(options => options.OnRetry = item =>
{
    var shieldName = item.Context.ShieldName;
    _ = Task.Run(() => Console.WriteLine(shieldName));
    return default;
});
```

Deferred or discarded work that captures the event itself is reported:

<!-- doc-test-diagnostic: KEV014 -->
```csharp
var shield = Shield.Retry(options => options.OnRetry = item =>
{
    _ = Task.Run(() => Console.WriteLine(item.Context.ShieldName)); // KEV014
    return default;
});
```

`KEV014` reports `Task.Run` and `ThreadPool.QueueUserWorkItem` calls, and discarded `Task` or
`ValueTask` work, that capture an event's `Context` or `Properties`. Because Kevlar awaits every
hook, using the event after an `await` inside an `async` hook is not deferred work and is not
reported. Deferred access can race with context pooling and observe state from a later execution,
so this diagnostic is a warning.

## Zero-tolerance diagnostics

Keep every Kevlar diagnostic enabled. When intent is safe but implicit, express that intent in code
so reviewers and analyzers see the same policy:

```csharp
var shield = Shield.Empty
    .WithDefaultHandling()
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
```

Do not add warning pragmas, `NoWarn`, or severity overrides. A suppression hides future regressions
at the same site; explicit configuration keeps intent checked as APIs and analyzers evolve.
