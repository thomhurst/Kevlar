---
sidebar_position: 1
slug: /intro
---

# Introduction

**Kevlar is fast, allocation-conscious resilience for .NET.** Retries, circuit breakers, timeouts, rate limiting, concurrency limits, hedging and fallbacks — composed through one fluent shield API.

```csharp
using Kevlar;

var shield = Shield
    .Timeout(TimeSpan.FromSeconds(30))                    // total budget for the whole operation
    .Retry(3)                                             // exponential backoff + jitter, out of the box
    .CircuitBreaker(5, breakDuration: TimeSpan.FromSeconds(30));

var user = await shield.ExecuteAsync(ct => LoadUserAsync(id, ct), cancellationToken);
```

Build a shield once, reuse it everywhere. Shields are **immutable and thread-safe**.

## Why Kevlar?

- **Intuitive first.** `Shield.When<TimeoutException>().Retry(3)` reads like what it does. No context pooling ceremony, no predicate-builder classes, no options objects for the simple cases — and full options objects when you want them.
- **Fast.** Outcomes flow between pipeline layers as structs instead of thrown exceptions; contexts are pooled internally; state-passing overloads eliminate closures; `ValueTask` end to end.
- **Production defaults.** `Shield.Retry(3)` gives you exponential backoff *with jitter* capped at 30s — the thing you'd have configured anyway.
- **Hard to hold wrong.** `Task` and `ValueTask` delegates both flow straight in; impossible chain orders throw at build time with a fix in the message; the `Kevlar.Analyzers` package flags cancellation and pipeline hazards at compile time.
- **Observable out of the box.** Every shield describes itself (`shield.ToString()` prints the whole pipeline) and publishes metrics through a built-in `Meter` — no telemetry package, no setup.
- **Composable.** Shields merge with `Wrap` and `Compose`, chain fluently, and stateful strategies (breakers, limiters) intentionally share their state wherever the same shield instance is reused.
- **Broad reach.** `netstandard2.0` (covers .NET Framework 4.6.2+) and `net10.0` targets.

## Packages

| Package | Purpose |
|---|---|
| `Kevlar` | The core: all strategies |
| `Kevlar.Extensions.DependencyInjection` | Named shields + `IKevlarRegistry` for Microsoft DI |
| `Kevlar.Extensions.Http` | `HttpClientFactory` integration, transient-fault handling, `Retry-After` support |
| `Kevlar.Analyzers` | Roslyn analyzers that catch resilience mistakes at compile time |

## Where to next?

- [Getting Started](getting-started.md) — install and build your first shield in five minutes.
- [Strategies](/docs/category/strategies) — every resilience behaviour in detail.
- [Coming from Polly?](polly-migration.md) — a 1:1 translation table.
