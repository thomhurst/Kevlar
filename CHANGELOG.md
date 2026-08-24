# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Strategy callback events now expose their position through public
  `KevlarContext.StrategyIndex`; the duplicate properties on limiter rejection events were removed.
  Circuit transitions use `CircuitBreakerStateChangedEvent` and carry execution context, while
  manual monitor transitions carry a detached context at index `-1`. Typed circuit-breaker
  duration generators receive `CircuitBreakerBreakDurationEvent<TResult>` with a directly stored
  outcome and failure statistics. Typed retry events likewise store their outcome without boxing.

## [1.0.0] - 2026-08-24

### Added

- `VoidShield` and `PartitionedVoidShield<TKey>` provide compile-time-safe void execution across
  core, dependency injection, rate limiting, testing, wrapping, and state snapshots.
- Result clauses include `WhenResultIsNull`, `OrResultIsNull`, `WhenResultIsDefault`, and
  `OrResultIsDefault` for nullable, value-type, and generic results.
- Reactive strategy options can replace ambient handling with `HandlesException` and typed
  `HandlesResult` predicates; testing descriptors expose the override.
- Pipeline descriptions display inherited handling clauses and local handling overrides.
- `Shield.Fallback(...)` can start an untyped chain, and `Shield<TResult>.Compose(...)` composes
  result-aware pipelines.
- Context-only synchronous and asynchronous execution overloads expose `KevlarContext` without
  requiring seeded state.
- Analyzer rules KEV001–KEV004 and KEV006–KEV011 cover ignored cancellation, ineffective handling,
  invalid ordering, untyped hedging, discarded builders and fluent calls, inherited handling,
  implicit default handling, and suspicious default-value matching.

### Changed

- `Kevlar.Testing` and `Kevlar.Extensions.RateLimiting` require the exact matching `Kevlar` package
  version. NuGet reports version-skew combinations as `NU1608` instead of allowing incompatible
  package combinations.
- Typed constant fallbacks use `FallbackTo(value)`; delegate factories continue to use
  `Fallback(...)`. Null fallback values are no longer ambiguous with delegate overloads.
- Handling clauses use `When...` to start and `Or...` to continue. `Shield.For<TResult>()` returns a
  `Shield<TResult>` directly.
- Typed and untyped retry, circuit-breaker, hedge, and fallback options are sealed sibling types
  with matching shared properties instead of inheritance. Shared configurator helpers need a
  separate typed counterpart such as `Action<RetryOptions<TResult>>`.
- `Shield.Wrap(...)` and `Shield.Compose(...)` seal ambient handling. Strategies appended after
  composition use default handling until another clause is declared.
- `RetryForever` has explicit parameterless and `Backoff` overloads; explicit `null` is rejected.
- Clause builders are immutable. Each `Or...` call returns a new builder and leaves the source
  builder unchanged.
- Debug builds reject access to a pooled `KevlarContext` after it has been returned.
- `Backoff.Constant` and explicit `maxDelay` values validate timer limits. Linear and exponential
  base delays are accepted beyond that limit, then computed delays clamp to their configured cap or
  the default one-day cap; custom delays clamp to the runtime timer limit.
- Custom strategies can declare `InvokesContinuationAtMostOnce`; the aggregate is exposed on
  `Shield`, `Shield<TResult>`, and `VoidShield`.
- Every NuGet package embeds the canonical icon, links release notes, and carries a package README
  with status badges. `Kevlar.Analyzers` is a development dependency.
- Queue capacity uses `QueueLimit` consistently across core strategies, adapters, dependency
  injection, and testing descriptors; shorthand parameters use `queueLimit`.
- `Outcome<T>.Exception` recognizes `KevlarProxyException` by type instead of reading
  `Exception.Data` on ordinary exception access.

**Upgrading from 0.x**

<!-- upgrade-from-0.x:start -->
| Before | After |
|---|---|
| `WhenDefault()` | `WhenResultIsDefault()` |
| `OrWhen(predicate)` | `Or(predicate)` |
| `HedgingOptions` | `HedgeOptions` |
| untyped `Fallback(...)` returned `Shield` | untyped `Fallback(...)` returns `VoidShield` |
| `FallbackWithNotifications(...)` or typed `onFallback` parameters | `Fallback(..., configure)` with `OnFallback` / `OnFallbackAsync` |
| shared `Action<RetryOptions>` used by typed shields | separate `Action<RetryOptions<TResult>>` configurator |
| `RetryForever(backoff: null)` | `RetryForever()` |
| ambient handling flowed past `Wrap`/`Compose` | `Wrap`/`Compose` seals the clause |
| `maxQueue` / `MaxQueue` | `queueLimit` / `QueueLimit` |
<!-- upgrade-from-0.x:end -->

The replacement forms compile together:

```csharp
_ = Shield.For<int>().WhenResultIsDefault().FallbackTo(-1);
_ = Shield.When<InvalidOperationException>().Or<TimeoutException>().RetryForever();
_ = Shield.When<InvalidOperationException>().RetryForever(Backoff.None);
_ = new HedgeOptions();
VoidShield recovery = Shield.Fallback(static _ => ValueTask.CompletedTask);
_ = Shield.Empty.Wrap(Shield.Retry(1));
```

### Removed

- Typed constant-value `Fallback(value)` overloads; use `FallbackTo(value)`.
- Nullable `RetryForever(Backoff? backoff = null)` overloads; use either explicit replacement.
- Mutable clause-builder behavior where ignored return values changed later chains.

### Fixed

- Circuit-breaker validation identifies the conflicting properties and reports public parameter
  names.
- Invalid fallback ordering is rejected at pipeline construction time; the void-only pipeline type
  rejects incompatible result-returning calls at compile time.
- Custom backoff arithmetic cannot create negative or unbounded runtime delays.

[Unreleased]: https://github.com/thomhurst/Kevlar/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/thomhurst/Kevlar/compare/v0.10.0...v1.0.0
