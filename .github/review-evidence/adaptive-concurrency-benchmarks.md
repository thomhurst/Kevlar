# Adaptive concurrency validation

Issue #509 adds an opt-in controller. Existing static admission is unchanged; its telemetry registration now shares an interface with adaptive instances and checks one additional observable gauge.

## Setup

- Baseline/control: `63eaebcdafaec7bd87f59bd48c98cbfe80234984`.
- Measured candidate: `90e8f7b913ae768497f1a279f1cbaf7d37204b07` on that baseline. The final branch rebases these changes onto `f003f2b4` (circuit-breaker probes/slow calls); the adaptive/static concurrency execution code is unchanged by that rebase.
- BenchmarkDotNet 0.15.8, default job, MemoryDiagnoser, .NET 10.0.12, SDK 10.0.401, Windows 11, Intel Core i7-12700K.
- Sequential baseline/candidate/control under the shared Redis performance reservation, held from 15:24 UTC. No other owned heavy workloads ran alongside measurements. Observed dotnet activity comprised this experiment and reusable MSBuild nodes; this is a shared workstation, not an isolated runner.
- Baseline: 2026-09-26 15:34:27–15:35:36 UTC; candidate: 15:35:54–15:37:13; control: 15:37:32–15:38:33. Times include process/build setup.
- A separate `--job Dry` run confirmed the new benchmark builds and executes before timing.

The static filters were `*ConcurrencyLimitBenchmarks.Kevlar_Uncontended*` and `*ConcurrencyLimitBenchmarks.Kevlar_WithHooks_Uncontended*`; the candidate filter was `*ConcurrencyLimitBenchmarks.Kevlar*`. Each command used `dotnet run --project benchmarks/Kevlar.Benchmarks -c Release -- [filters] --artifacts [output]`. Already-built runs used `--no-build` before `--`.

## Results

| Scenario | Baseline mean | Candidate mean | Control mean | Candidate throughput | Managed allocation |
| --- | ---: | ---: | ---: | ---: | ---: |
| Static, uncontended | 96.73 ns | 94.77 ns | 96.14 ns | 10.55 M calls/s | 0 B/call |
| Static, rejection callback configured | 93.89 ns | 95.63 ns | 93.90 ns | 10.46 M calls/s | 0 B/call |
| Adaptive, uncontended | — | 125.11 ns | — | 7.99 M calls/s | 0 B/call |

Candidate standard deviations were 0.993 ns, 0.380 ns, and 0.220 ns respectively. Static ordinary calls were 2.0% faster than the first baseline; the configured callback case was 1.9% slower. No material static-path regression was observed, but this does not establish a throughput improvement. Adaptive sampling costs about 30 ns more per uncontended call than the candidate's static limiter; it is explicitly opt-in. These microbenchmarks do not predict contended downstream throughput or controller convergence under production traffic.

The .NET 8 and .NET 10 allocation gates also passed, including warmed synchronous and synchronously completed asynchronous adaptive execution. The first observation for a new `TimeProvider` allocates its timestamp-origin registration; that initialization is outside steady-state allocation claims.

## Stress correctness

The stress harness now exercises adaptive admission independently of its existing Kevlar/Polly timing comparisons. Eight workers made 4,096 attempts: 2,986 were admitted, 1,110 rejected, and 157 admitted operations injected downstream failures. Peak active executions were eight, final limit was two, and no permit remained active. Assertions enforce configured bounds, accounting, and complete drain. This short run is a concurrency correctness check, not a production convergence or fairness claim.

Command: `dotnet run --project benchmarks/Kevlar.StressTests -c Release --no-build -- --duration 00:00:30 --warmup 00:00:00.1 --workers 8 --output [results.json]`.
