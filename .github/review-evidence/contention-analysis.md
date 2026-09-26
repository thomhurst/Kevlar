# Runtime contention investigation

Related to #512. The reproducible workflow and report tool are retained. No production
pooling change is included: the measured candidates did not consistently improve
throughput while preserving allocation behavior.

## Attribution

The [original Linux trace](https://github.com/thomhurst/Kevlar/actions/runs/36249176267)
recorded 866 contention starts, with zero lost events or unmatched starts/stops.
183 events (44.77 ms) came from timeout source arming/reset, and one event
(0.004 ms) came from circuit-breaker success recording. No sampled product stack
implicated the context pool, metrics, or random generation. The trace also contains
Polly and stress-harness events; total event counts are not Kevlar-only counts.

Reservoir 1.9.1 delegates source reset to CancellationTokenSource.TryReset.
The [.NET 10 timer implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/Timer.cs)
partitions timer queues by processor when timers are created. A timer keeps its
original queue when a pooled source moves between threads. This explains a possible
source of shared locks, but does not establish that partitioning the pool is beneficial.

## Rejected candidates

Selecting a pool on every rent using the current processor improved some stress
results but failed the unchanged allocation gate after processor migration
(0.02 B/op). Caching that processor selection per thread passed allocation gates,
but [both baseline controls](https://github.com/thomhurst/Kevlar/actions/runs/36250586060)
did not establish a consistent throughput improvement.

The final experiment gave each thread its own pool with capacity eight.
[Pinned comparison and trace](https://github.com/thomhurst/Kevlar/actions/runs/36251516978):
baseline `63eaebcdafaec7bd87f59bd48c98cbfe80234984`, candidate
`07e443297510d388e22ebd80e7cb469af5fbf804`. Sequential, unprofiled two-minute
baseline/candidate/baseline phases ran on one Ubuntu runner with four workers.

| Scenario | Workers | Baseline before ops/s | Candidate ops/s | Baseline after ops/s |
|---|---:|---:|---:|---:|
| Shared ratio pipeline | 1 | 3,756,265 | 3,837,232 | 3,799,184 |
| Shared ratio pipeline | 4 | 4,088,279 | 4,667,139 | 4,412,974 |
| Timeout and retry | 4 | 6,221,361 | 5,528,000 | 5,526,182 |
| Per-worker ratio pipeline | 4 | 6,260,802 | 5,238,078 | 6,737,145 |

The shared pipeline improved 5.8% over the final control, but the per-worker
pipeline regressed 16.3–22.3% against the two controls. Timeout/retry did not improve
over the final control. Allocation gates passed on .NET 8 and .NET 10, with no
threshold changes. Stress allocation numbers include amortized harness overhead.
The candidate trace recorded 32,019 starts with no lost or unmatched events;
timeout arming/reset still accounted for 31,500 events (142.13 ms). Trace counts
are diagnostic and must not be compared as normalized throughput measurements.

The evidence rejects the per-thread pool design. Further optimization needs a
different design and another pinned comparison; the profiling infrastructure makes
that investigation reproducible without shipping a throughput regression.
