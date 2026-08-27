# Cookbook

These recipes are starting points, not universal defaults. Choose handling predicates from the
failure contract of the dependency, keep total budgets bounded, and share a shield only when its
state should also be shared.

## Resilient HTTP client

Use the standard HTTP pipeline for an idempotent dependency. It owns total and per-attempt timeout,
retry, circuit-breaker, and response-disposal behavior:

```csharp
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

services.AddHttpClient("catalog", client =>
    client.BaseAddress = new Uri("https://catalog.example"))
    .AddStandardShield(options =>
    {
        options.TotalTimeout.Timeout = TimeSpan.FromSeconds(15);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        options.Retry.MaxRetries = 2;
        options.Retry.Backoff = Backoff.Exponential(
            TimeSpan.FromMilliseconds(200),
            maxDelay: TimeSpan.FromSeconds(2));
    });
```

Keep unsafe HTTP methods single-attempt unless the application has an idempotency contract. For
dynamic configuration, use the configuration-backed overload described in [HTTP resilience](http.md).

## Database query with recovery value

The fallback is outermost, so it can recover after retry, breaker, and timeout have finished. The
handling clause is explicit: programming and authentication failures are not retried or replaced.

```csharp
var databaseShield = Shield.For<int>()
    .When<TimeoutExceededException>()
    .Or<IOException>()
    .FallbackTo(-1)
    .Retry(3, Backoff.Exponential(
        TimeSpan.FromMilliseconds(100),
        maxDelay: TimeSpan.FromSeconds(1)))
    .CircuitBreaker(
        consecutiveFailures: 5,
        breakDuration: TimeSpan.FromSeconds(30))
    .Timeout(TimeSpan.FromSeconds(2));

var rowCount = await databaseShield.ExecuteAsync(
    static async cancellationToken =>
    {
        await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
        return 42;
    },
    cancellationToken);
```

Treat `-1` as a deliberate degraded result and surface it to callers and telemetry; do not make a
fallback indistinguishable from fresh data.

## Queue consumer with dead-letter fallback

For an idempotent message handler, place dead-letter publication outside the retrying work. The
fallback runs only after the inner policies cannot deliver the message:

```csharp
var consumerShield = Shield
    .When<MessagingException>()
    .Or<TimeoutExceededException>()
    .Fallback((exception, token) => deadLetter.PublishAsync(exception, token))
    .Retry(3, Backoff.Exponential(
        TimeSpan.FromMilliseconds(250),
        maxDelay: TimeSpan.FromSeconds(5)))
    .CircuitBreaker(
        consecutiveFailures: 10,
        breakDuration: TimeSpan.FromSeconds(20))
    .Timeout(TimeSpan.FromSeconds(10));

await consumerShield.ExecuteAsync(
    token => bus.PublishAsync(message, token),
    cancellationToken);
```

The broker's visibility timeout must exceed the total Kevlar budget. Make the handler idempotent,
because a process crash after the side effect but before broker acknowledgement can still cause
redelivery.

## Choosing order

Read chains from left to right as outermost to innermost. Put total timeout and fallback outside the
work they must observe; put per-attempt timeout inside retry. A circuit breaker usually sits outside
retry when one exhausted call should count once, and inside retry when each failed attempt should
contribute. Confirm the resulting tree with `shield.Describe()` and the
[composition guide](composition.md).
