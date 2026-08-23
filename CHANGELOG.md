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
  `HedgingOptions<TResult>` so result predicates remain strongly typed.

### Changed

- **Breaking:** `Shield.Wrap(...)` and `Shield.Compose(...)` now seal ambient handling clauses. Reactive strategies appended after composition use default handling unless a new clause is declared. Existing strategies inside composed shields keep their original handling.

### Added

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
