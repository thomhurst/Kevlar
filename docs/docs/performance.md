---
sidebar_position: 11
---

# Performance

Kevlar's design keeps the happy path cheap:

- **Failures travel as `Outcome<T>` structs** between strategies; exceptions are thrown once, at the boundary, with original stack traces preserved (`ExceptionDispatchInfo`). No throw/catch per pipeline layer.
- **Execution contexts are pooled** and recycled automatically — no context pool ceremony in your code, no per-call context allocation.
- **State-passing overloads** (`ExecuteAsync(state, static (s, ct) => …)`) make zero-closure call sites easy — see [Executing](executing.md#zero-closure-hot-paths).
- **Strategy chains are prebuilt linked nodes** — no per-call graph construction, no LINQ, no boxing of results on the success path.
- **`ValueTask` end to end** — synchronous completions (cache hits, fast paths) never allocate a `Task`.

## Benchmarks

The [Benchmarks](benchmarks.md) page compares Kevlar against Polly v8 across every strategy — happy paths, failure paths, and composed pipelines. It is regenerated automatically from CI runs, so the numbers there always reflect the current code.

The suite lives in [`benchmarks/`](https://github.com/thomhurst/Kevlar/tree/main/benchmarks) and uses BenchmarkDotNet:

```bash
dotnet run -c Release --project benchmarks/Kevlar.Benchmarks -- --filter '*'
```

As always with microbenchmarks: measure your own workload before optimizing around these numbers. The differences matter in tight loops and high-throughput services; they don't matter around a 50ms network call.

## Allocation regression gates

BenchmarkDotNet remains the trend and comparison tool. Pull requests also run a smaller deterministic allocation suite on Ubuntu with .NET 10. Each scenario is warmed up before measurement, then sampled five times over 10,000 operations with `GC.GetAllocatedBytesForCurrentThread()` so JIT, static initialization, pool seeding, test-runner work, and allocations on unrelated threads stay outside the per-operation count.

Documented synchronous-completion hot paths have a strict `0 B/op` budget. Paths that inherently create failures or parallel hedge attempts have explicit bounded budgets; those gates catch meaningful recurring regressions without pretending the failure path can be allocation-free. The allocation project is intentionally separate from coverage collection, which instruments assemblies and would contaminate the counts.

Run the same gates locally with:

```bash
dotnet run -c Release --project tests/Kevlar.AllocationTests -- --timeout 5m
```
