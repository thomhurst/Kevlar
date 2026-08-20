---
sidebar_position: 2
---

# Getting Started

## Install

```bash
dotnet add package Kevlar
```

Optional satellites:

```bash
dotnet add package Kevlar.Extensions.DependencyInjection   # named policies + IKevlarRegistry
dotnet add package Kevlar.Extensions.Http                  # HttpClientFactory integration
```

The core targets `netstandard2.0` (so .NET Framework 4.6.2+ works) and `net8.0`, with zero third-party dependencies.

## Your first policy

```csharp
using Kevlar;

var policy = Policy
    .Timeout(TimeSpan.FromSeconds(30))   // total budget for the whole operation
    .Retry(3)                            // exponential backoff + jitter, out of the box
    .CircuitBreaker(5, breakDuration: TimeSpan.FromSeconds(30));

var user = await policy.ExecuteAsync(ct => LoadUserAsync(id, ct), cancellationToken);
```

Three things to notice:

1. **Reading order is execution order.** The first strategy in the chain is the outermost — the timeout wraps the retries, which wrap the circuit breaker. Same rule as ASP.NET middleware.
2. **The defaults are the ones you'd have picked.** `Retry(3)` means exponential backoff with jitter, starting at 250ms and capped at 30s.
3. **Your delegate gets a cancellation token.** Always use the token you're handed — it's how timeouts and hedging cancel abandoned work.

## Reuse it everywhere

Policies are immutable and thread-safe. Build one, store it in a `static readonly` field or register it in [DI](dependency-injection.md), and use it for every call to that dependency:

```csharp
private static readonly Policy GitHubPolicy = Policy
    .Timeout(TimeSpan.FromSeconds(10))
    .Retry(3);

// Any result type, sync or async, through the same instance:
var repos = await GitHubPolicy.ExecuteAsync(ct => GetReposAsync(ct), ct);
var user  = await GitHubPolicy.ExecuteAsync(ct => GetUserAsync(ct), ct);
```

This matters for stateful strategies: a circuit breaker's state lives with the policy instance that created it. Reuse the instance and every call site shares one circuit; build a new instance and you get fresh state. See [Composition](composition.md).

## Deciding what counts as a failure

Reactive strategies (retry, circuit breaker, hedging, fallback) act on failures. By default that's **any exception except `OperationCanceledException`**. Narrow it with a handling clause:

```csharp
var policy = Policy
    .Handle<HttpRequestException>()
    .Or<TimeoutExceededException>()
    .Retry(5);
```

Want to treat certain *results* as failures too (HTTP 500s, say)? Lift into a typed policy with `For<T>`:

```csharp
var http = Policy.For<HttpResponseMessage>()
    .Handle<HttpRequestException>()
    .HandleResult(r => (int)r.StatusCode >= 500)
    .Retry(3);
```

Full details in [Handling failures](handling-failures.md).

## Next steps

- Browse the [strategy reference](/docs/category/strategies) — each strategy's options, defaults and semantics.
- Wire policies into [dependency injection](dependency-injection.md) or [HttpClient](http.md).
- [Test your policies](testing.md) without real waiting, using `TimeProvider`.
