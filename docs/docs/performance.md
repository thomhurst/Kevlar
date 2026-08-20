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
