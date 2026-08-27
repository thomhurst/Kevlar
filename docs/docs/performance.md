---
sidebar_position: 17
---

# Performance

Kevlar's design keeps the happy path cheap:

- **Failures travel as `Outcome<T>` structs** between strategies; exceptions are thrown once, at the boundary, with original stack traces preserved (`ExceptionDispatchInfo`). No throw/catch per pipeline layer.
- **Execution contexts are pooled** and recycled automatically — no context pool ceremony in your code, no per-call context allocation.
- **State-passing overloads** (`ExecuteAsync(state, static (s, ct) => …)`) make zero-closure call sites easy — see [Executing](executing.md#zero-closure-hot-paths).
- **Strategy chains are prebuilt linked nodes** — no per-call graph construction, no LINQ, no boxing of results on the success path.
- **`ValueTask` end to end** — synchronous completions (cache hits, fast paths) never allocate a `Task`.

## Tune budgets before mechanics

Start with dependency behavior, not nanoseconds. Set one total latency budget, decide how much of it
each attempt may consume, then fit retry delays and attempt count inside the remainder. A pipeline
that can schedule work after the caller's useful deadline wastes more capacity than a small
allocation ever will. Keep `HttpClient.Timeout` disabled when the standard HTTP shield owns total
and attempt timeouts.

Retries multiply load during an incident. Use bounded exponential backoff and jitter, cap additional
attempts, and combine retry with a circuit breaker when persistent faults should fail fast. Hedging
trades extra load for tail-latency reduction; reserve it for idempotent operations and measure the
loser rate before increasing parallel attempts.

## Share state deliberately

Breaker, limiter, and partition state lives in strategy instances. Reuse one shield when callers
should coordinate; build separate shields when isolation matters. Avoid constructing a stateful
pipeline per request, because that both allocates and prevents its history from becoming useful.
For tenant or endpoint isolation, prefer a bounded [`PartitionedShield`](partitioning.md) over an unbounded dictionary
of shields.

## Keep hot call sites allocation-conscious

Use state-passing overloads with a `static` callback when profiling shows closure allocation at a
high-volume call site:

```csharp
var result = await shield.ExecuteAsync(
    client,
    static (activeClient, token) => activeClient.GetUserAsync(1, token),
    cancellationToken);
```

Return an already-completed `ValueTask` for synchronous work. Do not wrap naturally asynchronous
I/O merely to force synchronous completion, and do not pool application objects until a profile
shows they dominate cost. Clear code around a network boundary is normally worth more than saving
a handful of bytes.

## Diagnose before changing

| Symptom | First checks |
|---|---|
| Rising p95 with low dependency latency | queue depth, limiter saturation, retry delays, total-timeout position |
| Traffic spike during dependency failure | retry count, jitter, breaker threshold, hedge loser rate |
| Breaker never opens | shield lifetime, handling predicates, result handling, partition key |
| Growing memory | unbounded partition keys, per-request pipeline construction, retained result bodies |
| Unexpected allocations | captured lambdas, yielding hooks, exception-heavy paths, HTTP content buffering |

Use application traces and metrics to identify the expensive path, then reproduce it with a focused
benchmark. Compare distributions and allocation counts across multiple runs; a single timing from a
shared machine is not a tuning decision.

## Benchmarks

The [Benchmarks](benchmarks.md) page compares Kevlar against Polly v8 across every strategy — happy paths, failure paths, and composed pipelines. It is regenerated automatically from CI runs, so the numbers there always reflect the current code.

The [stress tests](stress-tests.md) complement those short measurements with a 15-minute, same-process Kevlar/Polly run that records sustained throughput, allocations, garbage collections, and process memory.

The suite lives in [`benchmarks/`](https://github.com/thomhurst/Kevlar/tree/main/benchmarks) and uses BenchmarkDotNet:

```bash
dotnet run -c Release --project benchmarks/Kevlar.Benchmarks -- --filter '*'
```

As always with microbenchmarks: measure your own workload before optimizing around these numbers. The differences matter in tight loops and high-throughput services; they don't matter around a 50ms network call.

## Allocation regression gates

BenchmarkDotNet remains the trend and comparison tool. Pull requests also run a smaller deterministic allocation suite on Ubuntu with .NET 10. Each scenario is warmed up before measurement, then sampled five times over 10,000 operations. Synchronous-completion paths use `GC.GetAllocatedBytesForCurrentThread()` so test-runner work and unrelated threads stay outside the count. The parallel-hedge path waits for every canceled loser and uses `GC.GetTotalAllocatedBytes(precise: true)` so its asynchronous continuation and cleanup allocations are included regardless of which thread runs them.

Documented synchronous-completion hot paths have a strict `0 B/op` budget. Paths that inherently create failures or parallel hedge attempts have explicit bounded budgets; those gates catch meaningful recurring regressions without pretending the failure path can be allocation-free. The allocation project is intentionally separate from coverage collection, which instruments assemblies and would contaminate the counts.

Run the same gates locally with:

```bash
dotnet run -c Release --project tests/Kevlar.AllocationTests -- --timeout 5m
```
