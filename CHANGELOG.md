# Changelog

## Unreleased

### Breaking changes

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
