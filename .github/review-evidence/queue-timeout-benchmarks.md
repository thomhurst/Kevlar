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
