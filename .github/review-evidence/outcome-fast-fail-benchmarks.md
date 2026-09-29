# Outcome API fast-fail rejection benchmarks

Measured on 2026-09-29 with BenchmarkDotNet 0.15.8, its default job and MemoryDiagnoser,
.NET SDK 11.0.100-preview.7 host running runtime 10.0.12, Windows 11, Intel Core i7-12700K.
Commit base: `fe08d2714690484ec284fc9c3382560b1eb515bd` plus the new benchmark methods only; no
library code changed, so there is no baseline/candidate split.

One shared Redis performance reservation covered the build, tests, and the benchmark run. No
other heavy workload was launched by this agent during measurement. Local host activity is not
isolated by the reservation. These are local microbenchmarks, not production latency guarantees.

Command:

```bash
dotnet run -c Release --project benchmarks/Kevlar.Benchmarks -- \
  --filter '*CircuitBreakerBenchmarks*FastFail*' '*RateLimitBenchmarks*Rejected*' '*ConcurrencyLimitBenchmarks*Rejected*'
```

## Circuit breaker (manually isolated)

| Method | Mean | Allocated |
| --- | ---: | ---: |
| Kevlar_IsolatedFastFail (throw + catch) | 3,208.80 ns | 1,312 B |
| Polly_IsolatedFastFail (throw + catch) | 3,525.49 ns | 1,312 B |
| Kevlar_IsolatedFastFailOutcome | 86.25 ns | 144 B |
| Polly_IsolatedFastFailOutcome | 112.32 ns | 200 B |

## Rate limit (one-permit, one-hour budget already consumed)

| Method | Mean | Allocated |
| --- | ---: | ---: |
| Kevlar_RejectedFastFail (throw + catch) | 3,346.74 ns | 1,304 B |
| Polly_RejectedFastFail (throw + catch) | 46,219.26 ns | 46,060 B |
| Kevlar_RejectedFastFailOutcome | 93.98 ns | 136 B |
| Polly_RejectedFastFailOutcome | 31,577.15 ns | 40,325 B |

## Concurrency limit (single permit held, no queue)

| Method | Mean | Allocated |
| --- | ---: | ---: |
| Kevlar_RejectedFastFail (throw + catch) | 3,205.04 ns | 1,304 B |
| Polly_RejectedFastFail (throw + catch) | 39,184.88 ns | 44,075 B |
| Kevlar_RejectedFastFailOutcome | 77.22 ns | 136 B |
| Polly_RejectedFastFailOutcome | 31,074.27 ns | 38,329 B |

The outcome path removes 97-98% of Kevlar's rejection time and about 90% of its allocation. The
remaining 136-144 B is the rejection exception object itself. Polly's limiter rejections stay
expensive through `ExecuteOutcomeAsync`: about 31 us and 38-40 KB per rejection is spent before
any throw, inside Polly's limiter rejection path, so the outcome API saves only the throw itself.
