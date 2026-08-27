# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-08-27

Kevlar 1.0 establishes the stable Shield API: composable retry, timeout, circuit-breaker,
hedging, fallback, limiter, HTTP, dependency-injection, testing, logging, and analyzer support.
Pipelines stay immutable, allocation-conscious, observable, and explicit about execution order.

### Added

- The `Kevlar` package includes diagnostics-only Roslyn analyzers automatically. Rules KEV001–KEV014
  cover cancellation, ineffective handling, invalid ordering, state lifetime, fallback/result
  mismatches, untyped hedging, discarded builders and fluent calls, inherited and implicit default
  handling, synchronous hook hazards, and pooled-context capture.
- `Kevlar.Extensions.Logging` emits stable, structured `ILogger` events for retry, timeout,
  circuit-state, hedge, fallback, rejection, and callback-error activity. `WithLogging` decorates
  individual shields, while `AddKevlarLogging` applies to named, reloading, partitioned, and HTTP
  shields created through dependency injection.
- Non-generic `Outcome` and void `ExecuteOutcomeAsync` overloads make no-throw execution available
  to operations without a result. Synchronous `ExecuteOutcome` overloads cover void and typed work
  on `Shield` and `Shield<TResult>`; `Task`, `ValueTask`, and state-passing forms stay aligned.
- `ExecuteWithContextAsync` can invoke an `onCompleted` callback with final `KevlarProperties`
  before its pooled context is returned. The callback runs on success and failure without masking
  the execution outcome, and hedged executions expose the winning attempt's isolated properties.
- Result clauses include `WhenResultIsNull`, `OrResultIsNull`, `WhenResultIsDefault`, and
  `OrResultIsDefault` for nullable, value-type, and generic results.
- Reactive strategy options can replace ambient handling with `HandlesException` and typed
  `HandlesResult` predicates; testing descriptors expose the override.
- Pipeline descriptions display inherited handling clauses and local handling overrides.
- `Shield.Fallback(...)` can start an untyped chain, and `Shield<TResult>.Compose(...)` composes
  result-aware pipelines.
- Context-only synchronous and asynchronous execution overloads expose `KevlarContext` without
  requiring seeded state.
- Nested `ExecuteAsync` overloads accept a parent `KevlarContext`, carrying properties, operation
  keys, effective cancellation, and time-provider state across independently pooled pipelines.
- Handling-clause builders match their shield counterparts for configured timeouts, direct custom
  strategies, and void fallbacks that do not need the handled exception.
- Circuit-open, rate-limit, concurrency-limit, and timeout failures expose conventional public
  exception constructors while retaining strategy-specific metadata.

### Changed

- The support policy commits target-framework removals to major releases, gives obsolete APIs at
  least one minor release before removal, provides six months of previous-major security overlap,
  and documents dependency floors, package lockstep, cadence, maintenance, and roadmap ownership.
- HTTP handler replay and routing options are snapshotted when a pipeline is registered or created.
  Later mutations no longer alter live handlers; configuration reload remains the explicit update
  path and publishes a fresh complete snapshot.
- **Breaking:** `StandardHedgeShieldOptions` now groups strategy, request-handler, and endpoint
  routing settings under `TotalTimeout`, `Hedge`, `ConcurrencyLimit`, `CircuitBreaker`,
  `AttemptTimeout`, `Handler`, and `Routing`; configuration binding uses the same nested paths.
- `TimeoutExceededException` now derives directly from `KevlarException`. It no longer counts as
  an `ExecutionRejectedException`: fail-fast rejections mean the delegate did not run, while a
  timeout means it ran and exceeded its budget.
- Strategy callback events expose their position through public `KevlarContext.StrategyIndex`.
  Circuit transitions use `CircuitBreakerStateChangedEvent` and carry execution context, while
  manual monitor transitions carry a detached context at index `-1`. Typed breaker-duration and
  retry events store outcomes directly without boxing.
- Hedging supports a per-attempt `DelayGenerator` with access to attempt number, execution context,
  and elapsed time. Standard endpoint-aware HTTP hedging exposes the same adaptive delay hook, and
  `Kevlar.Testing` reports whether one is configured.
- Typed constant fallbacks use `FallbackTo(value)`; delegate factories continue to use
  `Fallback(...)`. Null fallback values are no longer ambiguous with delegate overloads.
- **Breaking:** typed constant-result clauses now use `WhenResultEquals(value)` and
  `OrResultEquals(value)`. The predicate forms remain `WhenResult(predicate)` and
  `OrResult(predicate)`, so `null` and `default` values no longer conflict with delegate overloads.
- Named DI registrations expose symmetric configuration overloads, typed
  `IShieldProvider<TResult>` snapshots, and `AddReloadingShield<TResult>`.
- `IKevlarRegistry` supports thread-safe late-bound `GetOrAdd`, `TryAdd`, and `Remove`, retries
  failed factories, reloads shields from named `IOptionsMonitor<TOptions>` values, and disposes
  resolved disposable strategies. Rate-limiter adapters can transfer limiter ownership explicitly.
- Invalid values supplied through strategy options throw `KevlarConfigurationException`; direct
  shorthand overloads continue to throw `ArgumentOutOfRangeException` with the invoked public
  parameter name.
- The System.Threading.RateLimiting adapter uses `UseRateLimiter(...)`,
  `RateLimiterAdapterRejectedEvent`, and `RateLimiterAdapterRejectedException`, separating adapter
  strategies from Kevlar's built-in `RateLimit(...)` strategy.
- `StandardHttpShieldOptions.CircuitBreaker` uses `CircuitBreakerOptions<HttpResponseMessage>`,
  exposing typed result predicates for the standard HTTP breaker.
- `MaxHedgedAttempts` counts additional attempts after the original, matching Polly and retry's
  `MaxRetries` convention. Its default is 1, so the default hedge makes up to 2 total attempts.
- Every strategy hook is a single `ValueTask`-returning property. The synchronous/asynchronous
  twins were merged: `OnRetryAsync`, `DelayGeneratorAsync`, `OnTimeoutAsync`,
  `TimeoutGeneratorSync`, `OnStateChangedAsync`, `BreakDurationGeneratorSync`, `OnHedgeAsync`,
  `HedgeOptions.DelayGeneratorAsync`, `OnFallbackAsync`, every `OnRejectedAsync`,
  `PartitionedShieldOptions.OnCreatedAsync` / `OnEvictedAsync`, and
  `StandardHedgeShieldOptions.HedgeDelayGeneratorAsync` were removed. Rewrite
  `OnRetry = e => Log(e)` as `OnRetry = e => { Log(e); return default; }` and
  `DelayGenerator = e => delay` as `DelayGenerator = e => new(delay)`; `async` lambdas are
  unchanged.
- Synchronous `Execute`, `ExecuteOutcome`, and `ExecuteWithContext` succeed whenever every hook
  completes synchronously. A hook that yields throws `NotSupportedException` at that call, naming
  the options type and hook. Multi-attempt hedging, `ValueTask`-returning fallback recovery
  delegates, and `UseRateLimiter` adapters remain unsupported in synchronous execution.
- `KEV012` reports `async` lambdas, `async` anonymous methods, and method groups naming `async`
  methods assigned to hooks on shields executed synchronously. Completed `ValueTask` delegates are
  not reported.
- `Kevlar.Testing`, `Kevlar.Extensions.RateLimiting`, and other packages that use core internals
  require the exact matching `Kevlar` version; NuGet reports skew as `NU1608`.
- HTTP retry and hedging stop after the first outcome when a request method or body cannot be
  replayed safely. The original response or exception is preserved while other resilience stages
  still observe the attempt.
- Runtime dependency floors remain compatible with .NET 8-era Microsoft.Extensions,
  System.Threading.RateLimiting, and TimeProvider packages; Reservoir is bounded to
  `[1.4.0, 2.0.0)`.
- Handling clauses use `When...` to start and `Or...` to continue. `Shield.For<TResult>()` returns
  a `Shield<TResult>` directly.
- Typed and untyped retry, circuit-breaker, hedge, and fallback options are sealed sibling types
  with matching shared properties instead of inheritance.
- The `Hedge` method, options, testing descriptor, strategy kind, and standard HTTP registration
  use one consistent `Hedge` stem.
- `Shield.Wrap(...)` and `Shield.Compose(...)` seal ambient handling. Strategies appended after
  composition use default handling until another clause is declared.
- `RetryForever` has explicit parameterless and `Backoff` overloads; explicit `null` is rejected.
- Clause builders are immutable. Each `Or...` call returns a new builder and leaves the source
  builder unchanged.
- Debug builds reject access to a pooled `KevlarContext` after it has been returned.
- `Backoff.Constant` and explicit `maxDelay` values validate timer limits. Computed delays clamp to
  their configured cap; custom delays clamp to the runtime timer limit.
- Retry jitter configuration uses `Jitter`; former Boolean values map to `Jitter.None` and
  `Jitter.Equal`.
- Custom strategies can declare `InvokesContinuationAtMostOnce`; the aggregate is exposed on
  `Shield` and `Shield<TResult>`.
- Every NuGet package embeds the canonical icon, links release notes, and carries a package README
  with status badges.
- Queue capacity uses `QueueLimit` consistently across core strategies, adapters, dependency
  injection, and testing descriptors; shorthand parameters use `queueLimit`.
- Pre-freeze API naming is consistent: partition and HTTP limits use `MaxPartitions` and
  `MaxBufferSize`; backoffs use `BaseDelay`; handling resets use `WithDefaultHandling()`; and
  `Kevlar.Testing` exposes `ShieldExecution.WaitForPendingAsync(task, ...)` as a static helper.
  The redundant partition `Remove` aliases and snapshot `ContractVersion` constants were removed.
  Satellite callback failures use `CallbackErrorKind.Custom` plus a stable `Source`, and HTTP
  execution-property keys live under `KevlarHttpKeys` in `Kevlar.Extensions.Http`.
- `Outcome<T>.Exception` recognizes `KevlarProxyException` by type instead of reading
  `Exception.Data` on ordinary exception access.

**Upgrading from 0.x**

Remove any direct `Kevlar.Analyzers` package reference; analyzers ship inside `Kevlar`. Upgrade all
`Kevlar.*` packages together when exact version constraints report `NU1608`.

<!-- upgrade-from-0.x:start -->
| Before | After |
|---|---|
| `WhenDefault()` | `WhenResultIsDefault()` |
| `OrDefault()` | `OrResultIsDefault()` |
| `OrWhen(predicate)` | `Or(predicate)` |
| `builder.When<TException>()` / `builder.When<TException>(predicate)` | `builder.Or<TException>()` / `builder.Or<TException>(predicate)` |
| `builder.When(predicate)` | `builder.Or(predicate)` |
| `builder.WhenResult(predicate)` / `builder.WhenResult(value)` | `builder.OrResult(predicate)` / `builder.OrResultEquals(value)` |
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
| `FallbackWithNotifications(...)` or typed `onFallback` parameters | `Fallback(..., configure)` with `OnFallback` |
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
| `Hedge(maxAttempts: n, ...)` / `options.MaxAttempts` | `Hedge(maxHedgedAttempts: n, ...)` / `options.MaxHedgedAttempts` |
| `MaximumPartitions` | `MaxPartitions` |
| `MaximumBufferSize` | `MaxBufferSize` |
| `Backoff.InitialDelay` / `Backoff.Exponential(initialDelay, ...)` | `Backoff.BaseDelay` / `Backoff.Exponential(baseDelay, ...)` |
| `PartitionedShield.Remove(key)` | `PartitionedShield.TryRemove(key)` |
| `task.WaitForPendingAsync(...)` | `ShieldExecution.WaitForPendingAsync(task, ...)` |
| `WhenAnyError()` | `WithDefaultHandling()` |
| `KevlarKeys.HttpRequestMethod` / `HttpRequestUri` | `KevlarHttpKeys.RequestMethod` / `RequestUri` |
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

### Deprecated

- The standalone `Kevlar.Analyzers` package is superseded by the analyzers bundled in `Kevlar`.
  Remove the separate package reference and use `Kevlar` as the replacement package.

### Removed

- `KEV013` and the `Kevlar.Analyzers.CodeFixes` assembly. `KEV012` covers synchronous-`Execute`
  callback hazards.
- Typed constant-value `Fallback(value)` overloads; use `FallbackTo(value)`.
- Nullable `RetryForever(Backoff? backoff = null)` overloads; use either explicit replacement.
- Mutable clause-builder behavior where ignored return values changed later chains.
- Public `StrategyNode`; pipeline nodes are implementation details and are now internal.

### Fixed

- State gauges now aggregate identical shield-name and strategy-index series across partitioned
  shields. Circuit breakers expose `kevlar.circuit_breaker.instances` counts grouped by state.
- Coupled satellite packages exact-pin their `Kevlar` dependencies. Partial upgrades of
  dependency injection, logging, and gRPC packages fail restore with `NU1608` instead of risking
  runtime failures from incompatible internals.
- Retry and hedging dispose superseded result values. `IAsyncDisposable` is preferred over
  `IDisposable`; disposal failures are isolated through `CallbackErrorKind.ResultDisposal`, while
  the selected terminal result remains caller-owned. The `netstandard2.0` package carries the
  required async-disposal runtime dependency.
- Ordinary `JsonContent` request bodies can now be retried without buffering. Replay remains
  conservative for `JsonContent` declared as `IAsyncEnumerable<T>` and other one-shot content.
  When HTTP replay safety disables a configured multi-attempt shield, Kevlar emits the
  `attempts_suppressed` event, Information log 1009, and `kevlar.http.replay_suppressed` metric.
- Circuit-breaker monitors and testing snapshots report `HalfOpen` as soon as break duration
  elapses. Stale outcomes cannot alter newer state generations, and exceptions that opened a
  circuit are released when it closes or resets.
- Circuit-breaker validation identifies conflicting properties and reports public parameter names.
- Invalid fallback ordering is rejected at pipeline construction. A shield containing a void
  fallback rejects result-returning execution at the execution boundary, and KEV005 diagnoses
  statically visible calls.
- Custom backoff arithmetic cannot create negative or unbounded runtime delays.

[Unreleased]: https://github.com/thomhurst/Kevlar/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/thomhurst/Kevlar/compare/v0.10.0...v1.0.0
