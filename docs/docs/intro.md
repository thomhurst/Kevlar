---
sidebar_position: 1
slug: /intro
---

# Introduction

**Kevlar is fast, zero-dependency resilience for .NET.** Retries, circuit breakers, timeouts, rate limiting, bulkheads, hedging and fallbacks — composed through one fluent, allocation-conscious policy API.

```csharp
using Kevlar;

var policy = Policy
    .Timeout(TimeSpan.FromSeconds(30))                    // total budget for the whole operation
    .Retry(3)                                             // exponential backoff + jitter, out of the box
    .CircuitBreaker(5, breakDuration: TimeSpan.FromSeconds(30));

var user = await policy.ExecuteAsync(ct => LoadUserAsync(id, ct), cancellationToken);
```

Build a policy once, reuse it everywhere. Policies are **immutable and thread-safe**.

## Why Kevlar?

- **Intuitive first.** `Policy.Handle<TimeoutException>().Retry(3)` reads like what it does. No context pooling ceremony, no predicate-builder classes, no options objects for the simple cases — and full options objects when you want them.
- **Fast.** Outcomes flow between pipeline layers as structs instead of thrown exceptions; contexts are pooled internally; state-passing overloads eliminate closures; `ValueTask` end to end.
- **Production defaults.** `Policy.Retry(3)` gives you exponential backoff *with jitter* capped at 30s — the thing you'd have configured anyway.
- **Composable.** Policies merge with `Wrap` and `Compose`, chain fluently, and stateful strategies (breakers, limiters) intentionally share their state wherever the same policy instance is reused.
- **Broad reach.** `netstandard2.0` (covers .NET Framework 4.6.2+) and `net8.0` targets. The core has zero third-party dependencies.

## Packages

| Package | Purpose |
|---|---|
| `Kevlar` | The core: all strategies, zero dependencies |
| `Kevlar.Extensions.DependencyInjection` | Named policies + `IKevlarRegistry` for Microsoft DI |
| `Kevlar.Extensions.Http` | `HttpClientFactory` integration, transient-fault handling, `Retry-After` support |

## Where to next?

- [Getting Started](getting-started.md) — install and build your first policy in five minutes.
- [Strategies](/docs/category/strategies) — every resilience behaviour in detail.
- [Coming from Polly?](polly-migration.md) — a 1:1 translation table.
