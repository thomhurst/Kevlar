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
