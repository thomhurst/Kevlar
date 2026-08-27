---
sidebar_position: 2
---

# Getting Started

Build and wire a production-ready shield in about ten minutes.

## Install

```bash
dotnet add package Kevlar
```

The core targets `netstandard2.0` (so .NET Framework 4.6.2+ works), `net8.0`, and `net10.0`.
See the canonical [package table](https://github.com/thomhurst/Kevlar#packages) for optional
integrations and testing support.

## Protect your first call

```csharp
using Kevlar;

var shield = Shield.Retry(3);

using var client = new HttpClient();
using var response = await shield.ExecuteAsync(
    ct => client.GetAsync("https://example.com", ct));
```

`Retry(3)` means up to 4 total attempts: the initial call plus three retries. Its default backoff is
exponential with equal jitter, starting at 250 ms and capped at 30 seconds. Always forward the
cancellation token passed to your delegate; timeout and hedging strategies use it to stop
abandoned work.

Bundled analyzer conventions: name the execution token `_` only for genuinely uncancellable work;
otherwise pass it through. Plain `Retry(3)` also raises an informational diagnostic; narrow
handling or set [`dotnet_diagnostic.KEV011.severity = none`](analyzers.md#kev011-implicit-default-handling)
when broad default handling is deliberate.

Shields are immutable and thread-safe. Build one and reuse it. Reuse also preserves state: calls
through the same shield share circuit-breaker and limiter state.

## Compose strategies

Strategies execute in reading order: the first strategy is the outermost, like ASP.NET middleware:

```csharp
var productionShield = Shield
    .Timeout(TimeSpan.FromSeconds(30))   // total budget for all attempts
    .When<HttpRequestException>()
    .Or<TimeoutExceededException>()
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30))
    .Timeout(TimeSpan.FromSeconds(5));   // budget for each attempt
```

Here, 30-second timeout wraps retry and circuit breaker. Final timeout applies separately to each
attempt. Handling clause is ambient: retry and circuit breaker both handle listed failures.
See [Composition](composition.md) and [Handling failures](handling-failures.md) for full rules.

## Wire production services

Install Microsoft dependency-injection and `HttpClientFactory` integrations:

```bash
dotnet add package Kevlar.Extensions.DependencyInjection
dotnet add package Kevlar.Extensions.Http
```

Add named shields and resilient HTTP clients in `Program.cs`:

```csharp
using Kevlar;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddShield("database", Shield
    .Timeout(TimeSpan.FromSeconds(10))
    .Retry(3));

services.AddHttpClient("catalog", client =>
    client.BaseAddress = new Uri("https://catalog.example.com"))
    .AddStandardShield();

using var serviceProvider = services.BuildServiceProvider();
```

`AddShield` registers a reusable named shield. `AddStandardShield` installs a production HTTP
pipeline with total and per-attempt timeouts, retry, and circuit breaker. Continue with
[Dependency injection](dependency-injection.md) or [HTTP resilience](http.md) to resolve and
customize them. Registration extensions live in `Microsoft.Extensions.DependencyInjection`, so
ASP.NET Core projects get them through implicit usings without importing a Kevlar package namespace.

## How to test it

Use `FakeTimeProvider` to advance retry and timeout delays instantly, then inspect pipeline shape,
telemetry, and strategy state with `Kevlar.Testing`. Follow [Testing](testing.md) for executable
examples instead of waiting on wall-clock timers.

## Next steps

- Browse the [API reference](pathname:///api/index.html) for every public type and member.
- Browse the [strategy reference](/docs/category/strategies) for options, defaults, and semantics.
- Add [logging](logging.md) and [observability](observability.md) before production rollout.
- See the [exceptions reference](exceptions.md) for strategy and satellite-package failures.
