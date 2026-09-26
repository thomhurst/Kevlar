# Timeout source thread-local tier experiment

Related to #512. This candidate is rejected; it is not qualified for merging.

Baseline: `02502dbdd36519064bd269ff19a28d4b813d5807`.
Candidate: `a5f0784c344be46ebb9b76e0bef8ef8c3d58396c`.
Both workflows use sequential baseline/candidate/baseline phases on their own
Ubuntu 24.04 runner with .NET 10.0.12. The candidate enables Reservoir's existing
one-object thread-local tier for timeout sources, retaining a bounded shared
fallback for nesting and migration.

## Unprofiled stress comparison

[Trace and two-minute comparison phases](https://github.com/thomhurst/Kevlar/actions/runs/36254146046).

| Scenario | Workers | Baseline before ops/s | Candidate ops/s | Baseline after ops/s |
|---|---:|---:|---:|---:|
| Shared ratio pipeline | 1 | 2,210,253 | 2,271,274 | 2,200,901 |
| Shared ratio pipeline | 4 | 4,282,695 | 4,148,269 | 4,389,158 |
| Timeout and retry | 4 | 5,533,508 | 6,063,551 | 6,005,456 |
| Per-worker ratio pipeline | 4 | 4,234,489 | 4,203,917 | 4,595,321 |

The shared four-worker pipeline is 3.1–5.5% slower than both controls. The
per-worker pipeline is 0.7–8.5% slower, with substantial control drift. Timeout
and retry improve 1.0–9.6%, but this does not establish a general improvement.
Stress allocations are approximately 0.0001–0.0002 B/op and include amortized
harness costs; the unchanged allocation gates pass on both .NET 8 and .NET 10.

The candidate trace contains 1,069 starts, zero lost events, and zero unmatched
starts or stops. Kevlar timeout stacks account for 164 events and 47.15 ms;
the other 905 events are in Polly stacks. Timer arming/reset remains the sampled
Kevlar contention source. Trace event counts are diagnostic, not normalized
throughput results.

## BenchmarkDotNet comparison

[Pinned microbenchmarks](https://github.com/thomhurst/Kevlar/actions/runs/36254147823).
AMD EPYC 9V74; BenchmarkDotNet 0.15.8. All warmed measurements allocate zero bytes.

| Method | Baseline before ns | Candidate ns | Baseline after ns |
|---|---:|---:|---:|
| Empty | 19.67 | 19.61 | 19.31 |
| TimeoutSuccess | 225.04 | 226.56 | 228.34 |
| NestedTimeoutSuccess | 423.44 | 401.08 | 397.83 |
| RetrySuccess | 159.89 | 155.25 | 163.65 |
| RetryHandledResult | 306.53 | 321.26 | 302.58 |
| HedgePrimaryWins | 325.50 | 325.76 | 324.33 |
| ExplicitChild | 414.12 | 398.08 | 401.63 |

The direct timeout result falls between controls. Nested timeout does not beat
the final control. RetryHandledResult changes without executing TimeoutStrategy,
which demonstrates measurement sensitivity outside the modified path; it cannot
be attributed to timeout pooling alone. No consistent benefit justifies the added
source lifecycle implementation. Linux and Windows build/test jobs pass, but
functional correctness does not override the performance acceptance failure.

The existing profiling tools remain on main from #560. Further work should target
timer scheduling itself and preserve cancellation races, provider semantics,
nesting, and zero-allocation success paths before another acceptance experiment.
