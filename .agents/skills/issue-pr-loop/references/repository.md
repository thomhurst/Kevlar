# Kevlar

Read [AGENTS.md](../../../../AGENTS.md) for commands, test matrix, commit style, and docs conventions.

- Preserve supported target frameworks from the project files. Forward cancellation and preserve analyzer `KEV001` semantics when changing execution overloads.
- Shields are immutable/thread-safe; reused instances share strategy state. Strategy, pooling, and composition changes need concurrency coverage.
- Shield execution, pipelines, `Outcome<T>`, pooled contexts, delay helpers, state-passing overloads, and HTTP handlers require relevant before/after benchmarks with throughput and allocations in the PR. Material regressions block merging; preserve synchronous `ValueTask` paths and avoid added closures, boxing, contention, or scans during review/simplification.
- No Aspire AppHost. Integration tests use in-process fakes and loopback HTTP; Docker serves only shared lock Redis.
