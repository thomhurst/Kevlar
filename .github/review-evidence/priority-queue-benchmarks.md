# Priority queue validation

## Initial isolated comparison

[Run 36259479703](https://github.com/thomhurst/Kevlar/actions/runs/36259479703) ran baseline, candidate, and baseline sequentially on one GitHub-hosted Ubuntu 24.04.5 runner (AMD EPYC 7763, .NET 10.0.12, SDK 10.0.401, BenchmarkDotNet 0.15.8).

- Baseline: `85bb0aca5e356dc88a359d6b54d354acab170a19`.
- Candidate: `3c92805b432460ea87e481474074b942e688afc4`.
- Filter: `*Queue*Benchmarks*`; default job, one process launch per case, MemoryDiagnoser; dry runs first.
- Candidate fixture copying was disabled because priority options do not exist in the baseline. The existing `QueueAdmissionBenchmarks.cs` fixture is byte-identical in both revisions. Priority cases run only on the candidate.

| Existing default path | Baseline before | Candidate | Baseline after | Allocation |
|---|---:|---:|---:|---:|
| Concurrency | 162.6 ns | 165.7 ns | 165.0 ns | 0 B |
| Concurrency with queue capacity | 164.7 ns | 166.7 ns | 165.8 ns | 0 B |
| Rate | 179.6 ns | 169.2 ns | 166.7 ns | 0 B |
| Rate with queue capacity | 174.9 ns | 186.2 ns | 177.5 ns | 0 B |

Opt-in priority admission, including configured queue deadlines, measured **169.2 ns / 0 B** for concurrency and **187.0 ns / 0 B** for rate limiting. These are uncontended measurements, not queued throughput measurements or promises of zero allocation while waiting.

The default rate queue is 8.7-11.3 ns (4.9-6.5%) slower than both controls. This initially blocked performance acceptance and prompted the investigations below. The existing rate admission algorithm is unchanged; the shared metrics registration changes from the concrete rate strategy to an internal state interface. Other default paths show small absolute deltas or baseline process variation. CPU-frequency strings reported by BDN vary between phases and do not establish the cause.

[Focused investigation 36260120072](https://github.com/thomhurst/Kevlar/actions/runs/36260120072) captures JIT disassembly at depth four and measures the two existing rate fixtures on candidate `5d8c9831e680be9a749aaee66f9e62eb4506cebc` against the same baseline. The intervening runtime change only bounds queued timer waits to one millisecond; the measured default and uncontended paths and benchmark fixtures are unchanged. The focused run completed on an Intel Xeon Platinum 8370C / Ubuntu 24.04.5 / .NET 10.0.12 runner:

| Default path | Baseline before | Candidate | Baseline after | Allocation |
|---|---:|---:|---:|---:|
| Rate | 139.4 ns | 136.5 ns | 138.8 ns | 0 B |
| Rate with queue capacity | 152.7 ns | 152.3 ns | 154.2 ns | 0 B |

Both benchmark entry methods are identical across phases after normalizing absolute process addresses. Depth-four disassembly does not follow the indirect generic dispatch into `RateLimitStrategy`; it does not prove every transitive instruction is identical. The reported transitive code sizes differ with traversal/JIT coverage.

This run did not reproduce the AMD slowdown, but results from different machines are not averaged and do not rule out a machine-specific effect. The three-process-launch comparison below quantified the process variation; measurement defaults remain one process launch.

## Three-launch rate investigation

[Run 36260700924](https://github.com/thomhurst/Kevlar/actions/runs/36260700924) compares baseline `85bb0aca` and candidate `f0854392` on AMD EPYC 7763, the same CPU model as the initial run, with three process launches per case. Default rate queue admission measured **184.9 / 182.4 / 179.0 ns**, all **0 B**. Per-process means were 177.812/178.879/196.667 ns before, 178.535/192.575/175.375 ns for the candidate, and 186.230/175.779/175.576 ns after. Slower processes occur in both controls and the candidate. The candidate's pooled mean is inside the control range, so the initial rate regression is not consistent across launches.

## Fresh-main comparison and concurrency investigation

After #574 merged, [run 36261056301](https://github.com/thomhurst/Kevlar/actions/runs/36261056301) compared main `091c3427aadd5f18723a832fb962b1da6ee251ec` against `76a2785a5922397514f1abd4a77d579cd78f5b7f`. The shared execution engine changed in main, so the earlier comparison alone did not cover the combined revision. This sequential A-B-A run uses AMD EPYC 7763, Ubuntu 24.04.5, .NET 10.0.12, SDK 10.0.401, BenchmarkDotNet 0.15.8, and three process launches per case. Fixtures remain unchanged and candidate fixture copying remains disabled.

| Default path | Baseline before | Candidate | Baseline after | Allocation |
|---|---:|---:|---:|---:|
| Concurrency | 169.4 ns | 164.3 ns | 166.8 ns | 0 B |
| Concurrency with queue capacity | 164.9 ns | 178.2 ns | 168.4 ns | 0 B |
| Rate | 171.4 ns | 169.5 ns | 167.6 ns | 0 B |
| Rate with queue capacity | 177.3 ns | 175.9 ns | 177.5 ns | 0 B |

Candidate-only priority admission measured **163.7 ns** for concurrency and **181.7 ns** for rate, both **0 B**. These remain uncontended measurements.

Rate queue performance passes this comparison. Default concurrency queue admission is 5.8-8.0% slower than the controls and initially blocked acceptance pending the investigation below. Its candidate process means are 187.919/165.832/179.892 ns, compared with 165.019/163.040/166.692 ns before and 166.621/172.320/165.815 ns after. The candidate's standard deviation is 9.16 ns; one process matches controls while two are slower. The default concurrency algorithm is unchanged; its only class diff exposes an existing rejection helper internally for the new strategy.

[Focused investigation 36262202036](https://github.com/thomhurst/Kevlar/actions/runs/36262202036) retains these exact revisions and measures only the default concurrency queue fixture, with five process launches and depth-four JIT disassembly. The completed five-launch comparison measured **166.3 / 165.2 / 164.5 ns**, all **0 B**, on AMD EPYC 7763 with the same software versions. Standard deviations were 5.63 / 2.43 / 2.40 ns. Per-process means were 164.630/174.374/160.399/171.281/161.006 ns before, 163.375/168.082/167.859/162.298/163.933 ns for the candidate, and 169.286/162.699/163.487/163.287/164.089 ns after. The candidate's pooled mean lies between the controls.

The benchmark entry method is 592 bytes in each phase and identical after normalizing absolute process addresses (SHA-256 prefix `6db0d9ab83504f06`). The captured 679-byte total also includes the generated delegate, its constructor, and `Shield.Name`; it does not cover indirectly dispatched strategy internals. This evidence supports process/JIT variance as an explanation for the inconsistent timing, without proving a specific cause or a universal speedup.

**Performance acceptance passes.** Both initially slower paths were investigated with repeated launches, preserving all earlier results. The final rate and concurrency comparisons do not show a consistent material regression. Runtime and fixture files are unchanged from measured head `76a2785a`; subsequent commits only update this evidence. Main's later #575 changes retry/hedge budgets without changing the measured queue paths.

## Functional and documentation checks

- Release solution build: zero warnings/errors.
- Core suites at `3c92805b`: 1,636 tests on .NET 8 and 1,671 on .NET 10 passed locally, including deterministic eviction, FIFO ties, cancellation, timeout, delayed timer dispatch, timer-creation failure, synchronous execution, telemetry, and concurrent permit accounting.
- Existing queue-timeout tests also run with priority queues enabled.
- Allocation gates: four tests pass on each target, including both priority paths at zero bytes per uncontended execution.
- Compatibility asset tests: 25 tests pass on .NET 10, including priority eviction/cancellation for both limiters through the netstandard asset.
- Testing package: 81 tests pass on .NET 10.
- Documentation builds and existing UI tests pass remotely. Samples now store reusable shields and forward cancellation; the corrected documentation analyzer rerun passes on Linux. The documentation site and its ten UI tests pass remotely; four desktop/mobile screenshots are included.
- The final Release solution rebuild passes with zero warnings/errors. All 18 priority tests pass on each of .NET 8 and .NET 10 at `5d8c9831`, including the added sub-millisecond regression.

The shared local performance reservation covered local builds/tests/docs work. Local workloads were deferred when another agent held the reservation. Timing acceptance uses the remote comparison, not local wall-clock timings.
