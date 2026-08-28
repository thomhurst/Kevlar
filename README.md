# Kevlar

[![NuGet version](https://img.shields.io/nuget/v/Kevlar.svg)](https://www.nuget.org/packages/Kevlar)
[![NuGet downloads](https://img.shields.io/nuget/dt/Kevlar.svg)](https://www.nuget.org/packages/Kevlar)
[![CI](https://github.com/thomhurst/Kevlar/actions/workflows/ci.yml/badge.svg)](https://github.com/thomhurst/Kevlar/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/thomhurst/Kevlar.svg)](https://github.com/thomhurst/Kevlar/blob/main/LICENSE)
[![Docs](https://img.shields.io/badge/docs-Kevlar-7c3aed.svg)](https://thomhurst.github.io/Kevlar/docs/getting-started)

**Fast, allocation-conscious resilience for .NET.** Kevlar brings retries, circuit breakers,
timeouts, rate limiting, concurrency limiting, hedging and fallbacks together in a fluent API.

Resilience code should explain how a call is protected, not make you decode a framework. With
Kevlar, you build an immutable `Shield`, reuse it, and use it with ordinary delegates.

[Documentation](https://thomhurst.github.io/Kevlar/docs/getting-started) ·
[Strategies](https://thomhurst.github.io/Kevlar/docs/category/strategies) ·
[API Reference](https://thomhurst.github.io/Kevlar/api/index.html) ·
[Benchmarks](https://thomhurst.github.io/Kevlar/docs/benchmarks)

## Get started

```bash
dotnet add package Kevlar
```

Install all coupled `Kevlar.*` packages at the same version. NuGet reports partial upgrades with
`NU1605` or `NU1608`; see the [package lockstep policy](https://thomhurst.github.io/Kevlar/docs/support-policy#kevlar-package-lockstep).

```csharp
using Kevlar;

var shield = Shield.Retry(3);

using var client = new HttpClient();
using var response = await shield.ExecuteAsync(
    ct => client.GetAsync("https://example.com", ct));
```

`Retry(3)` means three retries after the initial call: up to 4 total attempts. It uses exponential
backoff with equal jitter by default. The cancellation token passed to your delegate is
important—it is how timeouts and abandoned attempts stop the underlying work.

When you combine strategies, the first strategy is the outermost, just like ASP.NET middleware:

```csharp
var productionShield = Shield
    .Timeout(TimeSpan.FromSeconds(30))
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
```

That reads in execution order: the 30-second timeout wraps the retries, which wrap the circuit
breaker.

Build shields once and reuse them. They are immutable and thread-safe. Reuse also matters for
stateful strategies: calls made through the same shield share its circuit breaker and limiter state.

## Why Kevlar?

- **The common case stays small.** Start with `Shield.Retry(3)`; use options and callbacks when the
  situation genuinely needs them.
- **Failures can be exceptions or results.** Retry an `HttpRequestException`, an HTTP 500 response,
  or both, without changing the shape of the pipeline.
- **Composition is explicit.** Chain strategies, or combine existing shields with `Wrap` and
  `Compose`. The first strategy is always the outermost.
- **State can be isolated by key.** [Partitioned shields](https://thomhurst.github.io/Kevlar/docs/partitioning)
  retain independent breaker, limiter, and queue state per tenant, endpoint, or other bounded key.
- **It is designed for hot paths.** Struct outcomes, pooled contexts, state-passing overloads and
  `ValueTask` keep overhead and allocations low. Browse the
  [benchmark suite](https://github.com/thomhurst/Kevlar/tree/main/benchmarks/Kevlar.Benchmarks) or the published comparative
  [BenchmarkDotNet results](https://thomhurst.github.io/Kevlar/docs/benchmarks).
- **Production concerns are built in.** Shields support `TimeProvider`, describe their own pipeline,
  publish metrics through the `Kevlar` meter, and can emit structured `ILogger` events through
  `Kevlar.Extensions.Logging`. Hook exceptions never replace the protected outcome; observe them
  through `KevlarDiagnostics.OnCallbackError`, logging, or `Kevlar.Testing.TelemetryRecorder`.
  Built-in analyzers catch cancellation and pipeline mistakes at compile time.

The core package targets `netstandard2.0`, `net8.0`, and `net10.0`.

## Runnable samples

Every sample is a small `net8.0;net10.0` application with a `--smoke` mode used by CI:

- [`samples/ConsoleRetry`](https://github.com/thomhurst/Kevlar/tree/main/samples/ConsoleRetry) — minimal retry and recovery.
- [`samples/WebApi`](https://github.com/thomhurst/Kevlar/tree/main/samples/WebApi) — ASP.NET Core, named DI shields, `AddStandardShield`, and metrics.
- [`samples/GrpcClient`](https://github.com/thomhurst/Kevlar/tree/main/samples/GrpcClient) — transient gRPC unary retries.
- [`samples/PartitionedWorker`](https://github.com/thomhurst/Kevlar/tree/main/samples/PartitionedWorker) — bounded per-tenant shield state.
- [`samples/ChaosTesting`](https://github.com/thomhurst/Kevlar/tree/main/samples/ChaosTesting) — deterministic, operation-scoped fault injection.

Build all samples with `dotnet build samples/Samples.slnx -c Release`, or follow the command in a
sample's README to run one directly.

## Choose what counts as failure

Reactive strategies handle ordinary exceptions by default, excluding cancellation, Kevlar's
fail-fast rejections, and fatal runtime failures. A handling clause lets you be more precise:

```csharp
var search = Shield.For<HttpResponseMessage>()
    .When<HttpRequestException>()
    .Or<TimeoutExceededException>()
    .OrResult(response => (int)response.StatusCode is 429 or >= 500)
    .Fallback((outcome, ct) => cache.GetCachedResultsAsync(ct))
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
```

**A clause is ambient.** It applies to the strategy it is attached to *and* to every reactive
strategy chained after it, until a new clause replaces it, `WithDefaultHandling()` resets it, or
`Wrap`/`Compose` seals it. Above, the fallback, the retry *and* the circuit breaker all react to
the same three conditions. Nothing repeats the predicate per strategy:

```csharp
var api = Shield
    .When<HttpRequestException>()
    .Retry(3)                        // retries HttpRequestException
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
    // the breaker inherits the clause above: only HttpRequestException counts toward tripping it
```

Typed shields keep result handling strongly typed, including callback events and `Outcome<T>`
values.

## Compose protection in reading order

The first strategy is the outermost, just like ASP.NET middleware:

```csharp
var shield = Shield
    .Timeout(TimeSpan.FromSeconds(30))  // total budget
    .Retry(3)                           // retry within that budget
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30))
    .Timeout(TimeSpan.FromSeconds(5));  // budget for each attempt
```

This rule makes the important questions visible: is a timeout per attempt or for the whole call?
Does fallback wrap retry? Are two clients meant to share one circuit? See
[composition](https://thomhurst.github.io/Kevlar/docs/composition) for `Wrap`, `Compose` and the
state-sharing rules.

## HTTP and dependency injection

`Kevlar.Extensions.Http` provides a ready-to-use `HttpClientFactory` pipeline:

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddHttpClient("api")
    .AddStandardShield();
```

The standard shield has a 30-second total timeout, three jittered retries that honour
`Retry-After`, a circuit breaker, and a 10-second timeout per attempt. You can configure every
part or supply your own shield. POST, PATCH, and custom methods remain single-attempt unless you
explicitly enable replay for operations that are safe to repeat. `Kevlar.Extensions.DependencyInjection` adds named,
configuration-bound shields and `IKevlarRegistry`.

## Packages

| Package | What it adds |
|---|---|
| [`Kevlar`](https://www.nuget.org/packages/Kevlar) | Core strategies, Shield API, and diagnostics-only analyzers |
| [`Kevlar.Chaos`](https://www.nuget.org/packages/Kevlar.Chaos) | Controlled latency, faults, outcomes and custom behaviour |
| [`Kevlar.Extensions.DependencyInjection`](https://www.nuget.org/packages/Kevlar.Extensions.DependencyInjection) | Named and configuration-bound shields for Microsoft DI |
| [`Kevlar.Extensions.Http`](https://www.nuget.org/packages/Kevlar.Extensions.Http) | `HttpClientFactory` integration, request replay and transient-fault handling |
| [`Kevlar.Extensions.Logging`](https://www.nuget.org/packages/Kevlar.Extensions.Logging) | Structured `ILogger` events for every built-in strategy |
| [`Kevlar.Extensions.Grpc`](https://www.nuget.org/packages/Kevlar.Extensions.Grpc) | gRPC client resilience for unary and streaming calls |
| [`Kevlar.Extensions.RateLimiting`](https://www.nuget.org/packages/Kevlar.Extensions.RateLimiting) | Adapters for `System.Threading.RateLimiting` and custom leases |
| [`Kevlar.Testing`](https://www.nuget.org/packages/Kevlar.Testing) | Pipeline assertions, state snapshots and deterministic time helpers |

## Where next?

- Follow the [getting-started guide](https://thomhurst.github.io/Kevlar/docs/getting-started).
- Browse the complete [API reference](https://thomhurst.github.io/Kevlar/api/index.html).
- Browse the [strategy reference](https://thomhurst.github.io/Kevlar/docs/category/strategies).
- Add [HTTP resilience](https://thomhurst.github.io/Kevlar/docs/http),
  [dependency injection](https://thomhurst.github.io/Kevlar/docs/dependency-injection), or
  [observability](https://thomhurst.github.io/Kevlar/docs/observability).
- Moving from Polly? The [migration guide](https://thomhurst.github.io/Kevlar/docs/polly-migration)
  maps the concepts side by side.

## Community

Read the [contribution guide](https://github.com/thomhurst/Kevlar/blob/main/CONTRIBUTING.md),
[security policy](https://github.com/thomhurst/Kevlar/blob/main/SECURITY.md),
[code of conduct](https://github.com/thomhurst/Kevlar/blob/main/CODE_OF_CONDUCT.md), and
[changelog](https://github.com/thomhurst/Kevlar/blob/main/CHANGELOG.md). Use the guided issue forms
for bugs, feature requests, and questions; report vulnerabilities privately.
