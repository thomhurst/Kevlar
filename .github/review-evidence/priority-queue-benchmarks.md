# Priority queue validation

## Initial isolated comparison â€” investigation pending

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

The default rate queue is 8.7â€“11.3 ns (4.9â€“6.5%) slower than both controls. Performance acceptance remains blocked pending investigation. The existing rate admission algorithm is unchanged; the shared metrics registration changes from the concrete rate strategy to an internal state interface. Other default paths show small absolute deltas or baseline process variation. CPU-frequency strings reported by BDN vary between phases and do not establish the cause.

[Focused investigation 36260120072](https://github.com/thomhurst/Kevlar/actions/runs/36260120072) captures JIT disassembly at depth four and measures the two existing rate fixtures on candidate `5d8c9831e680be9a749aaee66f9e62eb4506cebc` against the same baseline. The intervening runtime change only bounds queued timer waits to one millisecond; the measured default and uncontended paths and benchmark fixtures are unchanged. The focused run completed on an Intel Xeon Platinum 8370C / Ubuntu 24.04.5 / .NET 10.0.12 runner:

| Default path | Baseline before | Candidate | Baseline after | Allocation |
|---|---:|---:|---:|---:|
| Rate | 139.4 ns | 136.5 ns | 138.8 ns | 0 B |
| Rate with queue capacity | 152.7 ns | 152.3 ns | 154.2 ns | 0 B |

Both benchmark entry methods are identical across phases after normalizing absolute process addresses. Depth-four disassembly does not follow the indirect generic dispatch into `RateLimitStrategy`; it does not prove every transitive instruction is identical. The reported transitive code sizes differ with traversal/JIT coverage.

This run did not reproduce the AMD slowdown, but results from different machines are not averaged and do not rule out a machine-specific effect. A focused three-process-launch comparison of the default queued rate path will quantify process variation before acceptance. It reuses the bounded workflow input from #574; measurement defaults remain one process launch.

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
