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

Early numbers against Polly 8.7 (.NET 10, x64, happy path):

| Scenario | Kevlar | Polly v8 |
|---|---|---|
| Retry(3), success | **100 ns, 0 B** | 154 ns, 24 B |
| Timeout → Retry → Breaker, success | **272 ns** | 393 ns |

A successful call through a three-strategy pipeline costs roughly a quarter of a microsecond and allocates nothing.

## Reproduce

The benchmark suite lives in [`benchmarks/`](https://github.com/thomhurst/Kevlar/tree/main/benchmarks) and uses BenchmarkDotNet:

```bash
dotnet run -c Release --project benchmarks/Kevlar.Benchmarks -- --filter '*'
```

As always with microbenchmarks: measure your own workload before optimizing around these numbers. The differences above matter in tight loops and high-throughput services; they don't matter around a 50ms network call.
