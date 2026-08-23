# Changelog

## Unreleased

### Breaking changes

- Configure fallback notifications through `Fallback(..., configure)`. The pre-release `FallbackWithNotifications` methods and typed optional `onFallback` parameters were removed. Migration-only error overloads prevent old positional callback lambdas from silently binding as options configurators.

Handling clauses now use one spelling per position. `When…` starts a clause on `Shield` or
`Shield<TResult>`; only `Or…` continues it on a builder. `Shield.For<TResult>()` now returns
`Shield<TResult>` directly.

| Before | After |
|---|---|
| `Shield.When<A>().When<B>()` | `Shield.When<A>().Or<B>()` |
| `Shield.For<T>().Or<A>()` | `Shield.For<T>().When<A>()` |
| `ShieldBuilder<T> builder = Shield.For<T>()` | `Shield<T> shield = Shield.For<T>()` |

- `RetryOptions<TResult>` no longer inherits `RetryOptions`. Both types retain the same scalar
  property names and defaults, but helpers that accepted `RetryOptions` must configure typed retry
  options separately. Typed callback getters now return the exact delegates assigned to them.

### Changed

- **Breaking:** `Shield.Wrap(...)` and `Shield.Compose(...)` now seal ambient handling clauses. Reactive strategies appended after composition use default handling unless a new clause is declared. Existing strategies inside composed shields keep their original handling.
