# Changelog

## Unreleased

### Breaking changes

- Configure fallback notifications through `Fallback(..., configure)`. The pre-release `FallbackWithNotifications` methods and typed `onFallback` parameters were removed, including the migration-only error overloads that briefly replaced them. Every fallback shape now has exactly two overloads — bare and `Action<FallbackOptions>`/`Action<FallbackOptions<TResult>>` — on `Shield`, `Shield<TResult>`, `ShieldBuilder` and `ShieldBuilder<TResult>`.

Handling clauses now use one spelling per position. `When…` starts a clause on `Shield` or
`Shield<TResult>`; only `Or…` continues it on a builder. `Shield.For<TResult>()` now returns
`Shield<TResult>` directly.

| Before | After |
|---|---|
| `Shield.When<A>().When<B>()` | `Shield.When<A>().Or<B>()` |
| `Shield.For<T>().Or<A>()` | `Shield.For<T>().When<A>()` |
| `ShieldBuilder<T> builder = Shield.For<T>()` | `Shield<T> shield = Shield.For<T>()` |
| `builder.OrWhen(predicate)` | `builder.Or(predicate)` |
| `Shield.For<T>().WhenDefault()` | `Shield.For<T>().WhenResultDefault()` |
| `builder.OrDefault()` | `builder.OrResultDefault()` |

`OrWhen` is gone: `Or(Func<Exception, bool>)` now mirrors `When(Func<Exception, bool>)`, so the
untyped predicate has the same spelling in both clause positions. `WhenDefault`/`OrDefault` were
renamed to `WhenResultDefault`/`OrResultDefault` because their "default" is `default(TResult)`,
not the default *handling* that the neighbouring `WhenAnyError()` restores.

- `RetryOptions<TResult>` no longer inherits `RetryOptions`. Both types retain the same scalar
  property names and defaults, but helpers that accepted `RetryOptions` must configure typed retry
  options separately. Typed callback getters now return the exact delegates assigned to them.
- Typed circuit-breaker and hedging configuration now uses `CircuitBreakerOptions<TResult>` and
  `HedgeOptions<TResult>` so result predicates remain strongly typed.
- `HedgingOptions`/`HedgingOptions<TResult>` were renamed to `HedgeOptions`/`HedgeOptions<TResult>`
  so the strategy method and its options type share a stem, like `Retry`/`RetryOptions` and
  `Timeout`/`TimeoutOptions`. `Kevlar.Extensions.Http`'s `StandardHedgingShieldOptions` and
  `AddStandardHedgingShield` are unchanged.

### Changed

- **Breaking:** `Shield.Wrap(...)` and `Shield.Compose(...)` now seal ambient handling clauses. Reactive strategies appended after composition use default handling unless a new clause is declared. Existing strategies inside composed shields keep their original handling.
- Setting both `CircuitBreakerOptions.ConsecutiveFailures` and `FailureRatio` still throws, but the
  message now names both properties and states the fix. `ConsecutiveFailures` range errors report
  that property as the parameter name instead of `options`.
- `HandlesException`/`HandlesResult` documentation on every options type now leads with the fact
  that the property makes its strategy ignore the ambient `When…` clause, and points at
  `HandlingClause`. `ShieldBuilder`/`ShieldBuilder<TResult>` document the override from the clause side.

### Added

- `KEV008`: a fluent chaining call written as a statement, such as `shield.Retry(3);`. Shields are
  immutable, so the new shield the call returns is thrown away and nothing is configured.
  Discarded clause builders keep reporting as `KEV007`.
- Pipeline descriptions show handling. `ToString()`/`Describe()` now prefixes each run of strategies
  sharing a non-default clause with `[when …]` — for example
  `[when HttpRequestException | TimeoutExceededException] Retry(3, no delay) → CircuitBreaker(5 consecutive, break 30s)`
  — and marks a strategy whose options replaced the clause locally with `(local handling)`. Shields
  that use only default handling describe exactly as before.
- Debug builds of Kevlar enforce the pooled-context contract: after a `KevlarContext` goes back to
  the pool, its `CancellationToken`, `Properties`, `ShieldName`, `TimeProvider` and `IsSynchronous`
  throw `InvalidOperationException` until it is rented again. Release builds are unchanged and carry
  no extra check.
- `KEV007`: a `When…`/`Or…` handling clause that never reaches a reactive strategy — the
  `ShieldBuilder` is discarded, or a later `When…`/`WhenAnyError()` replaces the clause while only
  proactive strategies stood between them.
- `Shield.Fallback(…)` static factories, mirroring the four untyped `ShieldExtensions.Fallback`
  overloads, so a fallback can start a chain like every other strategy. Fallback-first is the
  valid order: it recovers what the strategies chained inside it could not.
- `Shield<TResult>.Compose(params Shield<TResult>[])`, the result-aware counterpart of
  `Shield.Compose`. Same semantics: first shield outermost, first non-null name and
  `TimeProvider` win, ambient handling clauses sealed.
- Reactive strategy options can replace ambient handling locally with `HandlesException` and, on
  typed options, `HandlesResult`. Testing descriptors expose `HasHandlingOverride`.
- Context-only `ExecuteWithContext`/`ExecuteWithContextAsync` overloads that take just the
  context-aware action, for callers that read `KevlarContext` without seeding properties. Available
  on `Shield` and `Shield<TResult>`, synchronous and asynchronous, `ValueTask` and `Task`.
- `KEV006` warns when `Hedge(...)` is added to an untyped `Shield`, `ShieldBuilder`, or the static
  `Shield.Hedge` factory: hedging runs the action concurrently more than once, so it must be
  idempotent unless a typed shield's result clause can pick the winning attempt.
- Every `Retry` overload and `MaxRetries` documents that the value counts retries, not attempts:
  `Retry(3)` makes up to 4 total attempts.
