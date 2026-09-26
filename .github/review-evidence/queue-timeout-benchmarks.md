# Queue-timeout admission measurements

[Pinned comparison](https://github.com/thomhurst/Kevlar/actions/runs/36253670725)
ran baseline/candidate/baseline sequentially on one Ubuntu runner with identical
`QueueAdmissionBenchmarks` fixtures. Baseline:
`02502dbdd36519064bd269ff19a28d4b813d5807`; candidate:
`fbe76855966f29c77beab713abc69a2e81ae7dce`.

| Method | Baseline before | Candidate | Baseline after | Allocated |
|---|---:|---:|---:|---:|
| Concurrency | 97.27 ns | 97.77 ns | 92.33 ns | 0 B/op |
| Concurrency with queue capacity | 89.11 ns | 92.89 ns | 90.15 ns | 0 B/op |
| Rate | 92.79 ns | 92.29 ns | 93.35 ns | 0 B/op |
| Rate with queue capacity | 106.95 ns | 105.11 ns | 101.47 ns | 0 B/op |

Rate paths remain within the control range or slightly faster. Concurrency differs
by 0.50–5.44 ns; the controls themselves differ by 4.94 ns. Concurrency with queue
capacity adds 2.74–3.78 ns. These small admission differences do not establish a
material throughput regression; no allocation threshold was raised.

These fixtures measure default immediate admission, including enabled queue capacity.
Configured queue timeouts create no timer on immediate admission, verified by tests
and unchanged zero-allocation gates on .NET 8 and .NET 10. Actually waiting already
allocates; a configured timeout additionally owns a linked cancellation source and
a TimeProvider timer until admission or rejection. The feature makes no claim to
reduce allocations for queued work.

The subsequent observability-contract test correction and this report change no
runtime or benchmark source. The earlier pre-rebase comparison is retained at
https://github.com/thomhurst/Kevlar/actions/runs/36253012783; it uses a different main
baseline and is not combined with these measurements.
# Post-adaptive-concurrency comparison

The rebase onto `4e41e545ca87e21f3f249e26fb5fa87aa898ad4d` requires fresh evidence.
[Run 36255569762](https://github.com/thomhurst/Kevlar/actions/runs/36255569762)
measured candidate `d1e183d957000d898003a90cd37a961cd1c57f43` on one Ubuntu runner,
AMD EPYC 7763, .NET 10.0.12, BenchmarkDotNet 0.15.8. All paths allocate zero bytes.

| Method | Baseline before ns | Candidate ns | Baseline after ns |
|---|---:|---:|---:|
| Concurrency | 164.3 | 165.6 | 168.4 |
| ConcurrencyQueue | 161.9 | 163.4 | 166.4 |
| Rate | 166.4 | 175.9 | 167.5 |
| RateQueue | 177.4 | 177.5 | 174.4 |

Concurrency remains within the control range. Rate without a queue is 8.4–9.5 ns
(5.0–5.7%) slower than both controls; acceptance remains blocked. The only changed
argument setup in that admission method is the added optional rejection-reason
argument. A follow-up retains the original two-argument rejection entry point
outside the hot method and requires another comparison. This is a code-generation
hypothesis, not a confirmed explanation of the measured regression.
