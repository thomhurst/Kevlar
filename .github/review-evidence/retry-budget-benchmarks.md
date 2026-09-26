# Shared retry budget validation

Issue #507 adds an opt-in, shared success/failure feedback budget. Existing shields without a
budget retain their retry and hedge behavior. Budget observations include terminal attempts,
handled results, and late hedge losers; caller/winner cancellation is excluded.

## Sequential performance comparison

Baseline `02502dbdd36519064bd269ff19a28d4b813d5807`; candidate
`ef1490e426a00475e7b382e254993c21fb9286e0`. Later changes add only tests and documentation.
Baseline, candidate, and baseline control ran sequentially from 16:22:20 to 16:28:02 UTC on
2026-09-26 while holding the shared Redis `performance` reservation. Ownership was verified
before and after measurement. No other active local build/test/benchmark process was observed;
resident MSBuild workers and normal desktop applications remained running.

Environment: Windows 11 25H2, Intel Core i7-12700K (12 cores / 20 logical processors),
.NET SDK 10.0.401, runtime 10.0.12, BenchmarkDotNet 0.15.8, default job and MemoryDiagnoser.
These are workstation measurements, not an isolated production workload.

The existing `DeadlineOverheadBenchmarks` fixture bodies are identical at both revisions.
The additive `RetryBudgetBenchmarks` fixture runs only on the candidate. All three additive
cases first passed a Dry run. Commands use `dotnet run --project benchmarks/Kevlar.Benchmarks
-c Release -- --filter ... --artifacts ...`; filters select only the methods listed below.

| Existing path | Baseline before | Candidate | Baseline after | Candidate throughput | Allocated |
|---|---:|---:|---:|---:|---:|
| Retry success | 71.98 ns | 74.23 ns | 73.63 ns | 13.47 M calls/s | 0 B |
| Retry handled result, then success | 144.60 ns | 147.19 ns | 146.75 ns | 6.79 M calls/s | 0 B |
| Hedge primary success | 204.10 ns | 204.96 ns | 201.59 ns | 4.88 M calls/s | 0 B |

Candidate differences against the two controls are +0.8–3.1%, +0.3–1.8%, and +0.4–1.7%
respectively. Absolute increases are at most 3.37 ns. These small shifts do not establish a
material regression; there is no allocation increase. They do not establish a speedup either.

| Opt-in budget path | Candidate | Throughput | Allocated |
|---|---:|---:|---:|
| Retry success at full balance | 73.24 ns | 13.65 M calls/s | 0 B |
| Hedge primary success at full balance | 200.34 ns | 4.99 M calls/s | 0 B |
| Two handled failures followed by a success refund | 214.38 ns | 4.66 M calls/s | 0 B |

Success at capacity needs only a volatile read; recovery exercises atomic balance updates.
The recovery operation makes three attempts and restores its original balance each time.
The existing handled-result fixture makes two attempts, so those two rows are not a direct
before/after comparison. These uncontended microbenchmarks do not predict throughput when many
threads update one budget simultaneously. Concurrent accounting is covered by bounded tests.

## Behavioral evidence

- Release build: zero warnings/errors.
- Core suites: 1,560 tests on .NET 8 and 1,594 on .NET 10 before the additional reload test.
- Allocation gates: four tests on each runtime, including synchronous/async budgeted retries
  and a budgeted hedge primary success.
- .NET Standard asset: 21 tests on .NET 10; existing .NET Framework compatibility executable passes.
- Regression tests began red for missing budget wiring and fractional floating-point drift.
  Integer thousandths make ten 0.1 refunds reach the exact threshold.
- Tests cover shared shields/partitions, terminal failures, rejected result values, cancellation,
  concurrent bounds, callback/backoff rechecks, disposable-result ownership, hedge drainage,
  late loser accounting, telemetry, configuration errors, and state preservation across reloads.

- Additional DI reload coverage: all three budget DI tests pass on .NET 10.
- Package layout, symbols, determinism, SourceLink, consumers, trimming, single-file publishing,
  and analyzer checks pass. NativeAOT validation is delegated to Linux CI.
- DocFX builds with zero warnings/errors; the documentation site builds and ten Playwright tests pass.
- Strict documentation checks compile 250 warning-clean snippets and verify 11 diagnostic examples
  with 13 exact errors on both .NET 8 and .NET 10. The initial check caught a missing DI import and
  an untyped hedge example; both were corrected before the passing run.
- Retry-budget and DI screenshots were inspected at 1440px and 390px with no page overflow.

CI results are recorded in the PR validation section.
