---
sidebar_position: 3
---

# Handling Failures

Reactive strategies — [retry](strategies/retry.md), [circuit breaker](strategies/circuit-breaker.md), [hedging](strategies/hedging.md), [fallback](strategies/fallback.md) — act on failures. Handling clauses tell them what a failure *is*.

The [exceptions reference](exceptions.md) lists every Kevlar exception and whether this default handles it.

## The default

With no handling clause, reactive strategies handle ordinary exceptions. They deliberately let
these outcomes propagate without retrying, opening a circuit, hedging, or falling back:

| Not handled by default | Why |
|---|---|
| `OperationCanceledException` | Cancellation is not a fault; handling it would fight the caller. |
| `CircuitOpenException`, `RateLimitExceededException`, `ConcurrencyLimitExceededException` | These are fail-fast rejections from another Kevlar strategy. Handling one can amplify overload or hide admission control. |
| `OutOfMemoryException`, `InsufficientExecutionStackException`, `StackOverflowException`, `ThreadAbortException`, `AccessViolationException` | These fatal runtime failures are not safe recovery signals. |

Other exceptions—including programming errors such as `NullReferenceException`, and
`TimeoutExceededException`—remain handled by default for compatibility. Narrow expected failures
with a clause in production pipelines. The built-in analyzer reports implicit default handling as
[`KEV011`](analyzers.md#kev011-implicit-default-handling).

An explicit clause can opt into an excluded exception when recovery is intentional:

```csharp
var queuePressure = Shield
    .When<RateLimitExceededException>()
    .Retry(2);

var allKevlarOutcomes = Shield.For<string>()
    .When<KevlarException>()
    .FallbackTo("unavailable");
```

When you execute with `ExecuteOutcomeAsync`, use `outcome.TryGetResult(out var result)` to
consume a successful result without throwing. If it returns `false`, the final captured failure
remains available through `outcome.Exception`.

## Exception clauses

```csharp
var shield = Shield
    .When<HttpRequestException>()                     // this exception type (and subtypes)
    .Or<TimeoutExceededException>()                     // or this one
    .Or(ex => ex is IOException { Message: var m } && m.Contains("pipe"))  // or any predicate
    .Retry(5);
```

- `When<TException>()` starts a clause matching `TException` and anything derived from it. `When<TException>(predicate)` narrows it further, and `When(predicate)` matches on any exception.
- `Or<TException>()` / `Or<TException>(predicate)` / `Or(predicate)` add alternatives to the clause. All alternatives OR together.

`Or` mirrors `When` exactly: the untyped `Or(predicate)` takes a `Func<Exception, bool>`, and a bare
lambda binds to it because there is no type argument to infer for the generic overload.

Clause position determines the vocabulary: `When…` starts a clause on a shield, while `Or…`
continues that clause on the returned builder. The compiler therefore enforces
`When<A>().Or<B>().Or(...)`.

## Clauses are ambient

A clause applies to the strategy it is attached to *and* to every reactive strategy chained after
it, until you write a new clause, call `WithDefaultHandling()`, or compose with `Wrap`/`Compose`:

<!-- doc-test-ignore: Uses an ellipsis for the application-specific fallback implementation. -->
```csharp
Shield
    .When<HttpRequestException>()      // clause #1
    .Retry(3)                          //   ← uses clause #1
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: breakDuration) // ← also clause #1
    .When<TimeoutExceededException>()  // clause #2 replaces #1 from here on
    .Fallback(...);                    //   ← uses clause #2
```

This is why most chains only need one clause, written once at the top—and why you never repeat a
`ShouldHandle` predicate per strategy like in Polly v8.

This executable check proves an unrelated exception does not count toward the inherited breaker
threshold:

<!-- doc-test-run: getting-started-ambient-clause -->
```csharp
var ambient = Shield
    .When<HttpRequestException>()
    .Retry(1, Backoff.None)
    .CircuitBreaker(consecutiveFailures: 3, breakDuration: TimeSpan.FromMinutes(1));

try
{
    await ambient.ExecuteAsync(_ => ValueTask.FromException(new ArgumentException("not transient")));
}
catch (ArgumentException)
{
}

try
{
    await ambient.ExecuteAsync(_ => ValueTask.FromException(new HttpRequestException("offline")));
}
catch (HttpRequestException)
{
}

await ambient.ExecuteAsync(_ => ValueTask.CompletedTask);
```

A clause that never reaches a reactive strategy does nothing at all. The built-in analyzer reports
that as [`KEV007`](analyzers.md#kev007-dead-handling-clause). It also marks strategies that
*inherit* a clause as the informational hint
[`KEV009`](analyzers.md#kev009-inherited-handling-clause), so the span above is visible in the
editor.

### Reset to default handling

Call `WithDefaultHandling()` to clear the ambient clause. Reactive strategies chained after it return
to their defaults: retry, circuit breaker, and hedge handle ordinary exceptions while excluding
cancellation, Kevlar's fail-fast rejections, and fatal runtime failures; fallback additionally
handles fail-fast rejections.

```csharp
var shield = Shield
    .When<HttpRequestException>()
    .Retry(3)                                      // handles HttpRequestException only
    .WithDefaultHandling()
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30)); // handles ordinary exceptions
```

`WithDefaultHandling()` preserves existing strategies, the shield name, and its `TimeProvider`; it only
changes handling for reactive strategies added afterwards. It is available on both `Shield` and
`Shield<T>`.

## Builders are immutable

Clause builders are immutable, exactly like shields. Every `Or…` returns a *new* builder holding the
terms so far plus the one just added, and leaves the builder it was called on untouched — so a
builder held in a variable can be branched into independent chains:

```csharp
var transient = Shield.When<HttpRequestException>();

var reads = transient.Or<TimeoutExceededException>()
    .Timeout(options => options.Timeout = TimeSpan.FromSeconds(10))
    .Retry(3);
var writes = transient.Or<IOException>().Retry(1);   // no TimeoutExceededException here
```

`reads` handles `HttpRequestException | TimeoutExceededException`, `writes` handles
`HttpRequestException | IOException`, and neither branch sees the other's term. The corollary is
that only the builder an `Or…` *returns* carries the new term: writing `builder.Or<TException>();`
as a statement adds nothing to anything, which the analyzer reports as
[`KEV007`](analyzers.md#kev007-dead-handling-clause).

## Result clauses

Sometimes failure isn't an exception — it's a well-formed response you don't like (an HTTP 500, an empty payload, a `Status = "Retry"` field). Lift into a typed shield with `For<T>` and add `WhenResult`:

```csharp
var http = Shield.For<HttpResponseMessage>()
    .When<HttpRequestException>()
    .OrResult(r => (int)r.StatusCode >= 500)
    .Retry(3);
```

Now a 503 response triggers a retry exactly as a thrown `HttpRequestException` would. The delegate's return value is inspected — nothing is thrown internally, the outcome just counts as a failure.

Typed builders add result alternatives with `OrResult(predicate)` / `OrResultEquals(value)`, and two shorthands for the most common check of all:

```csharp
Shield.For<User?>().WhenResultIsNull().Retry(2);      // retry when the result is null
// mid-chain: .OrResultIsNull() adds the same check to an existing clause
```

`WhenResultIsNull` / `OrResultIsNull` are constrained to reference types, which is the point: they
say what they match and cannot be written where they would surprise you.

For a value type or for generic code, `WhenResultIsDefault` / `OrResultIsDefault` match
`default(T)` instead — and there the check needs a second thought, because `0`, `false`, and an
empty struct are usually legitimate results rather than failures. The built-in analyzer raises that
question as the informational hint [`KEV010`](analyzers.md#kev010-default-result-clause-on-a-value-type):

```csharp
Shield.For<int>().WhenResultIsDefault().Retry(2);     // Is 0 really the failure?
Shield.For<int>().WhenResultEquals(-1).Retry(2);      // clean: the failing value, spelled out
```

All four are named after the `WhenResult` / `OrResult` family precisely so they cannot be confused
with `WithDefaultHandling()`, which resets *handling* to Kevlar's default.

## Context-aware clauses

Use a `HandlingEvent` predicate when handling depends on the current attempt, execution
properties, or strategy position. `Attempt` is zero-based for retry and hedging, and zero for
circuit breakers and fallbacks. Start with `WhenContext`, continue exception or outcome
alternatives with `OrContext`, and use `WhenResultContext` / `OrResultContext` when only successful
typed results should reach the predicate.

```csharp
var isRead = new KevlarKey<bool>("is-read");
var contextual = Shield
    .WhenContext(handling =>
        handling.Exception is TimeoutExceededException
        && handling.AttemptNumber < 2
        && handling.Context.Properties.GetOrDefault(isRead))
    .Retry(3, Backoff.None);

await contextual.ExecuteWithContextAsync(
    isRead,
    static (key, properties) => properties.Set(key, true),
    static (_, _) => ValueTask.FromException(
        new TimeoutExceededException(TimeSpan.FromSeconds(1))),
    cancellationToken);
```

On `Shield<TResult>`, context-aware predicates receive `HandlingEvent<TResult>`. Its typed
`Outcome` exposes either the exception or result without boxing. Use the
`HandlesExceptionWithContext` and `HandlesResultWithContext` option properties for a local
per-strategy override.

Every predicate shape follows the same failure contract. If an exception, result, or context-aware
predicate throws, that predicate is treated as not handled and later alternatives are still
evaluated. The original execution outcome remains unchanged. During shield execution, Kevlar
reports the predicate exception through `KevlarDiagnostics.OnCallbackError` with
`CallbackErrorKind.HandlingPredicate`, the `kevlar.callback_errors` counter with
`kevlar.callback.kind=handling_predicate`, telemetry, and structured logging. Predicate failures
therefore stay visible without changing retry, breaker, hedge, or fallback behavior.

The context-free `HandlingClause.ShouldHandle(in outcome)` overload has no active execution and
cannot emit execution diagnostics. Custom reactive strategies should pass their active
`KevlarContext` to the context-aware overload, as shown below.

## Per-strategy overrides

Use an options predicate when one reactive strategy needs different handling without changing the
ambient clause for its neighbors:

```csharp
var shield = Shield.When<HttpRequestException>()
    .Retry(3) // ambient clause
    .CircuitBreaker(options =>
    {
        options.ConsecutiveFailures = 5;
        options.HandlesException = exception => exception is TimeoutExceededException;
    })
    .Hedge(maxHedgedAttempts: 2, delay: TimeSpan.FromMilliseconds(100)); // ambient clause again
```

`HandlesException` is available on retry, circuit-breaker, hedging, and fallback options. Their
typed options also expose `HandlesResult`. If either property is set, that strategy ignores the
ambient clause completely: local override > ambient clause > default. Predicates are not merged.

Only what you list is handled. A result-only override does not handle exceptions, and an
exception-only override does not handle results. Prefer the ambient clause when several neighboring
strategies share the same rule; prefer a local override for a single exception to that rule or when
porting one Polly `ShouldHandle` predicate directly.

Which strategy ended up with which rule is visible at runtime: `shield.ToString()` prefixes each run
of strategies sharing a non-default clause with `[when …]` and marks a locally overridden strategy
`(local handling)`. See [pipeline descriptions](observability.md#pipeline-descriptions).

:::info Proactive strategies don't consult clauses
Timeouts, rate limits and concurrency limits don't care why something failed — they act on time and concurrency, not outcomes. Clauses only drive the reactive strategies.
:::

## Custom reactive strategies

Custom strategies opt into ambient handling through the `Use` factory overload:

<!-- doc-test-ignore: RetryOnceStrategy is defined on the Custom Strategies page. -->
```csharp
var shield = Shield.When<HttpRequestException>()
    .Use(clause => new RetryOnceStrategy(clause));
```

The factory runs once and receives a `HandlingClause`. Call its context-aware `ShouldHandle`
overload inside the strategy so exception and result rules stay aligned with the shield. See
[Custom Strategies](custom-strategies.md#consume-handling-clauses) for a complete implementation.

:::info Lifting preserves clauses; composition seals them
`shield.For<T>()`, `WithName(...)`, and `WithTimeProvider(...)` are same-chain copies, so they
preserve the ambient clause. `Wrap(...)` and `Shield.Compose(...)` are composition boundaries:
strategies already inside keep their original handling, but reactive strategies chained afterwards
use the default unless you declare a new local clause. Within one chain, `WithDefaultHandling()` explicitly
returns subsequent strategies to the default.
:::
