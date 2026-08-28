# Upgrading from 0.x

Kevlar 1.0 establishes the stable Shield API: composable retry, timeout, circuit-breaker,
hedging, fallback, limiter, HTTP, dependency-injection, testing, logging, and analyzer support.
Pipelines stay immutable, allocation-conscious, observable, and explicit about execution order.

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
| `RetryEvent.Attempt` / `RetryEvent<TResult>.Attempt` | `AttemptNumber` |
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
_ = Shield.When<InvalidOperationException>().Or<ArgumentException>().RetryForever();
_ = Shield.When<InvalidOperationException>().RetryForever(Backoff.None);
_ = new HedgeOptions();
Shield recovery = Shield.Fallback(static _ => ValueTask.CompletedTask);
_ = Shield.Empty.Wrap(Shield.Retry(1));
```
