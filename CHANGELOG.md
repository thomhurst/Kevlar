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
- Hedging supports synchronous and asynchronous per-attempt delay generators with access to the
  attempt number, execution context, and elapsed time. Standard endpoint-aware HTTP hedging
  exposes the same adaptive delay hooks, and `Kevlar.Testing` reports whether one is configured.
- `HedgeOptions<TResult>.ActionGenerator` is now a strongly typed delegate. Assign the generator
  directly instead of wrapping it with `HedgeActionGenerator.Create<TResult>(...)`; the erased
  wrapper remains available on untyped `HedgeOptions`. Typed generator events now expose the latest
  available `Outcome<TResult>`.
- Untyped `Fallback(...)` continues to return `Shield`. The pre-release untyped shield type family
  and its satellite overloads were removed. Result-returning execution through a void fallback is
  guarded by the restored `KEV005` analyzer; use `Shield.For<TResult>().Fallback(...)` for
  result-producing recovery.
- Configure fallback notifications through `Fallback(..., configure)`. The pre-release
  notification-specific methods and typed `onFallback` parameters were removed, including the
  migration-only error overloads that briefly replaced them. Every fallback shape now has exactly
  two overloads — bare and `Action<FallbackOptions>`/`Action<FallbackOptions<TResult>>` — on
  `Shield`, `Shield<TResult>`, `ShieldBuilder`, and `ShieldBuilder<TResult>`.
- Typed constant-value `Fallback(value)` is now `FallbackTo(value)` on `Shield<TResult>` and
  `ShieldBuilder<TResult>`. Delegate factories remain `Fallback(...)`. This makes null fallback
  values explicit and avoids ambiguity between value and delegate overloads.
- Named DI registrations now expose symmetric configuration overloads, typed
  `IShieldProvider<TResult>` snapshots, and `AddReloadingShield<TResult>`.
- Invalid values supplied through strategy options now throw `KevlarConfigurationException`;
  direct shorthand overloads continue to throw `ArgumentOutOfRangeException` with the invoked
  public parameter name.
- The System.Threading.RateLimiting adapter now uses `UseRateLimiter(...)`,
  `RateLimiterAdapterRejectedEvent`, and `RateLimiterAdapterRejectedException`. The distinct names
  separate adapter-backed strategies from Kevlar's built-in `RateLimit(...)` strategy.
- `StandardHttpShieldOptions.CircuitBreaker` now uses
  `CircuitBreakerOptions<HttpResponseMessage>`, exposing typed result predicates for the standard
  HTTP breaker.

## [1.0.0] - 2026-08-24

### Added

- Result clauses include `WhenResultIsNull`, `OrResultIsNull`, `WhenResultIsDefault`, and
  `OrResultIsDefault` for nullable, value-type, and generic results.
- Reactive strategy options can replace ambient handling with `HandlesException` and typed
  `HandlesResult` predicates; testing descriptors expose the override.
- Pipeline descriptions display inherited handling clauses and local handling overrides.
- `Shield.Fallback(...)` can start an untyped chain, and `Shield<TResult>.Compose(...)` composes
  result-aware pipelines.
- Context-only synchronous and asynchronous execution overloads expose `KevlarContext` without
  requiring seeded state.
- Analyzer rules KEV001–KEV011 cover ignored cancellation, ineffective handling, invalid ordering,
  state lifetime, void fallback/result mismatches, untyped hedging, discarded builders and fluent
  calls, inherited and implicit default handling, and suspicious default-value matching.
- Circuit-open, rate-limit, concurrency-limit, and timeout rejections derive from
  `ExecutionRejectedException`, which provides their common `RetryAfter` property. Concrete public
  exception types expose conventional constructors while retaining metadata-specific overloads.

### Changed

- `Kevlar.Testing` and `Kevlar.Extensions.RateLimiting` require the exact matching `Kevlar` package
  version. NuGet reports version-skew combinations as `NU1608` instead of allowing incompatible
  package combinations.
- HTTP retry and hedging stop after the first outcome when a request method or body cannot be
  replayed safely. The original response or exception is preserved without a retry delay or
  callback, while other resilience stages still observe the attempt.
- Runtime dependency floors remain compatible with .NET 8-era Microsoft.Extensions,
  System.Threading.RateLimiting, and TimeProvider packages; Reservoir is bounded to `[1.4.0, 2.0.0)`.
- Typed constant fallbacks use `FallbackTo(value)`; delegate factories continue to use
  `Fallback(...)`. Null fallback values are no longer ambiguous with delegate overloads.
- Handling clauses use `When...` to start and `Or...` to continue. `Shield.For<TResult>()` returns a
  `Shield<TResult>` directly.
- Typed and untyped retry, circuit-breaker, hedge, and fallback options are sealed sibling types
  with matching shared properties instead of inheritance. Shared configurator helpers need a
  separate typed counterpart such as `Action<RetryOptions<TResult>>`.
- The `Hedge` method, options, testing descriptor, strategy kind, and standard HTTP registration
  use one consistent `Hedge` stem.
- `Shield.Wrap(...)` and `Shield.Compose(...)` seal ambient handling. Strategies appended after
  composition use default handling until another clause is declared.
- `RetryForever` has explicit parameterless and `Backoff` overloads; explicit `null` is rejected.
- Clause builders are immutable. Each `Or...` call returns a new builder and leaves the source
  builder unchanged.
- Debug builds reject access to a pooled `KevlarContext` after it has been returned.
- `Backoff.Constant` and explicit `maxDelay` values validate timer limits. Linear and exponential
  base delays are accepted beyond that limit, then computed delays clamp to their configured cap or
  the default one-day cap; custom delays clamp to the runtime timer limit.
- Retry jitter configuration uses the `Jitter` enum. The former Boolean `false` maps to
  `Jitter.None`; `true` maps to `Jitter.Equal`.
- Custom strategies can declare `InvokesContinuationAtMostOnce`; the aggregate is exposed on
  `Shield` and `Shield<TResult>`.
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
| `OrDefault()` | `OrResultIsDefault()` |
| `OrWhen(predicate)` | `Or(predicate)` |
| `builder.When<TException>()` / `builder.When<TException>(predicate)` | `builder.Or<TException>()` / `builder.Or<TException>(predicate)` |
| `builder.When(predicate)` | `builder.Or(predicate)` |
| `builder.WhenResult(predicate)` / `builder.WhenResult(value)` | `builder.OrResult(predicate)` / `builder.OrResult(value)` |
| `builder.WhenDefault()` | `builder.OrResultIsDefault()` |
| `Shield.For<TResult>().Or<TException>()` / `.Or(predicate)` | `Shield.For<TResult>().When<TException>()` / `.When(predicate)` |
| `ShieldBuilder<TResult> builder = Shield.For<TResult>()` | `Shield<TResult> shield = Shield.For<TResult>()` |
| `HedgingOptions` | `HedgeOptions` |
| `HedgingStrategyDescriptor` | `HedgeStrategyDescriptor` |
| `StrategyKind.Hedging` | `StrategyKind.Hedge` |
| `StandardHedgingShieldOptions` | `StandardHedgeShieldOptions` |
| `AddStandardHedgingShield(...)` | `AddStandardHedgeShield(...)` |
| `VoidShield` / `VoidShieldBuilder` | `Shield` / `ShieldBuilder`; use `Shield.For<TResult>()` when recovery produces a result |
| `PartitionedVoidShield<TKey>` | `PartitionedShield<TKey>` |
| `FallbackWithNotifications(...)` or typed `onFallback` parameters | `Fallback(..., configure)` with `OnFallback` / `OnFallbackAsync` |
| shared `Action<RetryOptions>` used by typed shields | separate `Action<RetryOptions<TResult>>` configurator |
| `RetryForever(backoff: null)` | `RetryForever()` |
| ambient handling flowed past `Wrap`/`Compose` | `Wrap`/`Compose` seals the clause |
| `maxQueue` / `MaxQueue` | `queueLimit` / `QueueLimit` |
| `Kevlar.Extensions.DependencyInjection.BackoffKind` | `Kevlar.BackoffKind` |
| `jitter: false` / `RetryDefinition.Jitter = false` | `jitter: Jitter.None` / `RetryDefinition.Jitter = Jitter.None` |
| `jitter: true` / `RetryDefinition.Jitter = true` | `jitter: Jitter.Equal` / `RetryDefinition.Jitter = Jitter.Equal` |
| `RetryEvent.Attempt` / `RetryEvent<TResult>.Attempt` | `RetryNumber` |
| `HedgeEvent.Attempt` | `AttemptNumber` |
| adapter `.RateLimit(limiter)` | `.UseRateLimiter(limiter)` |
| `RateLimiterRejectedEvent` | `RateLimiterAdapterRejectedEvent` |
| adapter `RateLimitExceededException` | `RateLimiterAdapterRejectedException` |
| `StandardHttpShieldOptions.CircuitBreaker` as `CircuitBreakerOptions` | `CircuitBreakerOptions<HttpResponseMessage>` |
<!-- upgrade-from-0.x:end -->

The replacement forms compile together:

```csharp
_ = Shield.For<int>().WhenResultIsDefault().FallbackTo(-1);
_ = Shield.When<InvalidOperationException>().Or<TimeoutException>().RetryForever();
_ = Shield.When<InvalidOperationException>().RetryForever(Backoff.None);
_ = new HedgeOptions();
Shield recovery = Shield.Fallback(static _ => ValueTask.CompletedTask);
_ = Shield.Empty.Wrap(Shield.Retry(1));
```

### Removed

- Typed constant-value `Fallback(value)` overloads; use `FallbackTo(value)`.
- Nullable `RetryForever(Backoff? backoff = null)` overloads; use either explicit replacement.
- Mutable clause-builder behavior where ignored return values changed later chains.
- Public `StrategyNode`; pipeline nodes are implementation details and are now internal.

### Fixed

- Circuit-breaker validation identifies the conflicting properties and reports public parameter
  names.
- Invalid fallback ordering is rejected at pipeline construction time. A shield containing a void
  fallback rejects result-returning execution at the execution boundary, and KEV005 diagnoses
  statically visible calls.
- Custom backoff arithmetic cannot create negative or unbounded runtime delays.

[Unreleased]: https://github.com/thomhurst/Kevlar/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/thomhurst/Kevlar/compare/v0.10.0...v1.0.0
