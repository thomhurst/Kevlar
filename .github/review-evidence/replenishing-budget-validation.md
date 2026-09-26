# Replenishing additional-attempt allowance (#500)

`RetryBudget.CreateReplenishing` adds a fixed-window allowance alongside the existing feedback constructor. Initial attempts are free. Retry and hedge acquire one token atomically at final admission; outcomes do not refund or debit this mode. The budget owns its monotonic clock and refills lazily, without a timer. Existing instances can be shared through named DI and partition factories.

## Correctness and compatibility

Runtime candidate: `16a0440fb375df2b62a2eaa628e88b74ed97d036`, based on main `85bb0aca5e356dc88a359d6b54d354acab170a19`.

- Release solution build: zero warnings/errors.
- Full core suites: 1,624/.NET 8 and 1,659/.NET 10 passed. The subsequently added circuit-rejection case and simplified typed test were validated in the 21-case focused suite on both runtimes.
- Allocation gates: four tests on each runtime, including allowance retry success, hedge primary success, and actual additional retry admission.
- Netstandard asset compatibility: 24 tests on .NET 10; .NET Framework 4.8 compatibility passed, including deterministic refill.
- Package layout, symbols, deterministic payloads, SourceLink, consumers, trimming/single-file, and analyzer checks passed. NativeAOT runs in Linux CI.
- DocFX: no warnings. npm clean install and production build passed. All 10 Playwright tests passed. Desktop/mobile screenshots were inspected, with no page overflow.
- Strict package-based documentation compilation: 255 snippets on each of .NET 8 and .NET 10, plus 11 diagnostic examples with 13 exact errors on each runtime.

The initial compile failed with CS9124 because a primary-constructor capacity parameter was both captured and used as a field initializer. Commit `16a0440f` stores capacity explicitly. The new circuit-rejection test initially expected a second rejected retry; existing default classification treats circuit-open rejection as terminal. The corrected test confirms the one admitted retry remains charged. No production contract or test gate was weakened.

## Performance

[Sequential isolated A-B-A](https://github.com/thomhurst/Kevlar/actions/runs/36259932201) is pending. It pins the SHAs above, retains identical existing fixtures, and covers default retry success/handled-result/hedge-primary paths plus the three existing feedback-budget fixtures. Candidate-only allowance fixtures are excluded from the old baseline. Performance acceptance remains pending until results are assessed.

Local opt-in diagnostics: Windows 11, i7-12700K, .NET 10.0.12, BenchmarkDotNet 0.15.8, default job. These are added-feature costs, not evidence that default paths are unchanged.

| Case | Mean | Allocated |
|---|---:|---:|
| RetrySuccess | 77.70 ns | 0 B |
| HedgePrimaryWins | 205.18 ns | 0 B |
| HandledResultRecovery (three attempts) | 534.15 ns | 0 B |

The allowance capacity is intentionally large enough to measure actual extra attempts without depletion; windows refill during the benchmark. Raw artifacts: `C:/git/kevlar-500-allowance-benchmarks`. The shared performance reservation covered local validation and measurement, approximately 17:40–17:50 UTC on 2026-09-26. All other owned heavy workloads completed before the benchmark. No competing owned workloads ran during measurement.