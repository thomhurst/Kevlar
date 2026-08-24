# Changelog

## Unreleased

### Breaking changes

- Untyped `Fallback(...)` now changes the fluent chain's static type from `Shield` to `VoidShield`.
  `VoidShield` exposes only void execution overloads, and every later fluent method preserves that
  type. Result-returning execution, `For<TResult>()`, typed wrapping, and mixed static composition
  therefore fail at compile time instead of reaching a void fallback that cannot produce a value.
  `ShieldBuilder.Fallback(...)` similarly returns `VoidShield`; its later `When...` clauses use
  `VoidShieldBuilder`. Use `Shield.For<TResult>().Fallback(...)` for result-producing recovery.
- Configure fallback notifications through `Fallback(..., configure)`. The pre-release `FallbackWithNotifications` methods and typed `onFallback` parameters were removed, including the migration-only error overloads that briefly replaced them. Every fallback shape now has exactly two overloads — bare and `Action<FallbackOptions>`/`Action<FallbackOptions<TResult>>` — on `Shield`, `Shield<TResult>`, `ShieldBuilder` and `ShieldBuilder<TResult>`.
- Typed constant-value `Fallback(value)` is now `FallbackTo(value)` on `Shield<TResult>` and
  `ShieldBuilder<TResult>`. Delegate factories remain `Fallback(...)`. This makes null fallback
  values explicit and avoids ambiguity between value and delegate overloads.

Handling clauses now use one spelling per position. `When…` starts a clause on `Shield` or
`Shield<TResult>`; only `Or…` continues it on a builder. `Shield.For<TResult>()` now returns
`Shield<TResult>` directly.

| Before | After |
|---|---|
| `Shield.When<A>().When<B>()` | `Shield.When<A>().Or<B>()` |
| `Shield.For<T>().Or<A>()` | `Shield.For<T>().When<A>()` |
| `ShieldBuilder<T> builder = Shield.For<T>()` | `Shield<T> shield = Shield.For<T>()` |
| `builder.OrWhen(predicate)` | `builder.Or(predicate)` |
| `Shield.For<T>().WhenDefault()` | `Shield.For<T>().WhenResultIsDefault()` |
| `builder.OrDefault()` | `builder.OrResultIsDefault()` |
| `Shield.For<T>().Fallback(value)` | `Shield.For<T>().FallbackTo(value)` |
| `ConcurrencyLimit(..., maxQueue: n)` / `options.MaxQueue` | `ConcurrencyLimit(..., queueLimit: n)` / `options.QueueLimit` |

`OrWhen` is gone: `Or(Func<Exception, bool>)` now mirrors `When(Func<Exception, bool>)`, so the
untyped predicate has the same spelling in both clause positions. `WhenDefault`/`OrDefault` were
renamed to `WhenResultIsDefault`/`OrResultIsDefault` because their "default" is `default(TResult)`,
not the default *handling* that the neighbouring `WhenAnyError()` restores. (An earlier pre-release
build spelled these `WhenResultDefault`/`OrResultDefault`; the `Is` makes the reading unambiguous.)

- Retry, circuit-breaker, hedge, and fallback options are sealed sibling types. Each typed/untyped
  pair retains the same shared property names and scalar defaults, but helpers that accept an
  untyped options type must configure typed options separately. Typed callbacks and result
  predicates keep their exact compile-time types.
- `HedgingOptions`/`HedgingOptions<TResult>` were renamed to `HedgeOptions`/`HedgeOptions<TResult>`
  so the strategy method and its options type share a stem, like `Retry`/`RetryOptions` and
  `Timeout`/`TimeoutOptions`. `Kevlar.Extensions.Http`'s `StandardHedgingShieldOptions` and
  `AddStandardHedgingShield` are unchanged.
### Changed

- Every NuGet package now embeds the canonical Kevlar icon, links release notes, and carries a
  NuGet-safe README with status badges. `Kevlar.Analyzers` is marked as a development dependency
  so it does not flow from a packed consumer library.
- `Kevlar.Testing` and `Kevlar.Extensions.RateLimiting` now require the exact matching `Kevlar`
  package version. These packages still use core implementation details for structured inspection
  and metrics, so NuGet now reports unsafe version-skew combinations as `NU1608` (and rejects them
  when NuGet warnings are errors) instead of silently allowing runtime compatibility failures.
- Custom strategies can override `Strategy.InvokesContinuationAtMostOnce`; the same aggregate
  value is now exposed on `Shield<TResult>` as well as `Shield` and `VoidShield`.
- **Breaking:** `Shield.Wrap(...)` and `Shield.Compose(...)` now seal ambient handling clauses. Reactive strategies appended after composition use default handling unless a new clause is declared. Existing strategies inside composed shields keep their original handling.
- `Backoff.Custom` documents the clamping the retry path already applied to its delegate's result: a
  negative delay becomes zero, a delay above the runtime timer limit becomes that limit, and the
  retry's own `MaxDelay` caps what is left.
- The composition guide states that `outer.Wrap(inner)` and `Shield.Compose(outer, inner)` are the
  same operation — same strategy order, same first non-null name and `TimeProvider`, same sealed
  clause — so there is no semantic difference to hunt for.
- Setting both `CircuitBreakerOptions.ConsecutiveFailures` and `FailureRatio` still throws, but the
  message now names both properties and states the fix. `ConsecutiveFailures` range errors report
  that property as the parameter name instead of `options`.
- `HandlesException`/`HandlesResult` documentation on every options type now leads with the fact
  that the property makes its strategy ignore the ambient `When…` clause, and points at
  `HandlingClause`. `ShieldBuilder`/`ShieldBuilder<TResult>` document the override from the clause side.
- `ShieldBuilder` and `ShieldBuilder<TResult>` are immutable. Every `Or…`/`OrResult…`/
  `OrResultIsDefault` returns a *new* builder carrying the terms so far plus the one just added, and
  leaves the builder it was called on untouched. Branching two chains from one stored builder is now
  safe — each branch gets only its own terms — and a shield already built from a builder still keeps
  the clause it was built with. The corollary is that only the builder an `Or…` *returns* carries
  the new term; `builder.Or<T>();` written as a statement adds nothing, which `KEV007` reports.
- **Breaking:** `RetryForever` is now two overloads — `RetryForever()` and `RetryForever(Backoff)` —
  on `Shield` (static), `ShieldExtensions`, `Shield<TResult>`, `ShieldBuilder` and
  `ShieldBuilder<TResult>`, replacing the single `RetryForever(Backoff? backoff = null)`. This
  matches `Retry(int, Backoff)`, whose `Backoff` was already non-nullable. The parameterless
  overload still uses `Backoff.Default`; passing `null` explicitly is no longer legal.

### Added

- `VoidShield` and `PartitionedVoidShield<TKey>`, plus void-aware dependency-injection registry,
  rate-limiter adapter, inspection, and state-snapshot overloads. `outer.Wrap(voidShield)` and
  `voidShield.Wrap(inner)` preserve the void-only type. The former `KEV005` analyzer is removed
  because invalid result execution is now rejected by the C# compiler.
- `WhenResultIsNull()` / `OrResultIsNull()` on `ShieldResultExtensions`: the null-result clause
  `WhenResultIsDefault`/`OrResultIsDefault` was really written for, constrained to reference types
  (`where TResult : class?`) so it cannot be written where `default` is an ordinary value. They
  render as `[when null result]`. `WhenResultIsDefault`/`OrResultIsDefault` stay, for value types
  and generic code.
- `KEV010`: an informational hint on `WhenResultIsDefault`/`OrResultIsDefault` written for a
  non-nullable value type, where `default(T)` — `0`, `false`, an empty struct — is as often a
  legitimate result as a failure. `Nullable<T>` results and generic code are not flagged, and like
  `KEV009` the hint never fails a build.
- `KEV009`: an informational hint marking each reactive strategy that inherits a handling clause
  declared earlier in its chain, so the clause's span is visible in the editor. It is `Info`
  severity — the inheritance is by design, and the hint never fails a build. Proactive strategies
  and strategies with a local `HandlesException`/`HandlesResult` override are never flagged.
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
