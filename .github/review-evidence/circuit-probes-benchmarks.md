# Circuit-breaker probe and slow-call validation

Measured on 2026-09-26 with BenchmarkDotNet 0.15.8, its default job and MemoryDiagnoser,
.NET SDK 10.0.401/runtime 10.0.12, Windows 11, Intel Core i7-12700K.
The baseline is `0974184195c203abc6a397b65643a088a26a6dd1`; the candidate is
`c6fc3d6da0f861485d9a8006766c9eceeff7a510`. The candidate's additional base commit
`7b458e72` only updates published stress documentation.

A single shared Redis performance reservation covered preparation and the sequential
baseline/candidate/baseline experiment. No other heavy workload was launched by this agent
during measurement. Local host activity is not isolated by the reservation. These are local
microbenchmark measurements, not end-to-end throughput or a production latency guarantee.

## Results

| Existing path | Baseline A mean | Candidate mean | Baseline control mean | Candidate throughput | Allocation per operation |
| --- | ---: | ---: | ---: | ---: | ---: |
| Ratio closed happy path | 97.95 ns | 99.71 ns | 95.37 ns | 10.03 M ops/s | 0 B |
| Dynamic duration configured | 104.07 ns | 107.88 ns | 112.77 ns | 9.27 M ops/s | 0 B |
| State callback configured | 107.55 ns | 108.05 ns | 109.30 ns | 9.26 M ops/s | 0 B |

Throughput is the reciprocal of the candidate mean, not a separate concurrent-load measurement.
The ordinary ratio path is 1.8% above the first baseline and 4.6% above the control. The two
configured paths fall inside the range between baseline runs. In particular, the dynamic-duration
baseline moves by 8.4%, so sub-nanosecond conclusions are not supported. Existing paths retain
zero measured per-call allocation; these results do not establish zero CPU overhead.

With slow-call detection enabled, successful fast calls measure **211.81 ns/op**, **4.72 M ops/s**,
and **0 B/op**. This includes duration measurement and window evaluation; it adds about 112 ns
against the candidate's ordinary ratio path. The threshold is one second, so this case measures
ongoing detection without opening the breaker. Its standard deviation is 10.93 ns, versus
1.09–2.60 ns for the candidate's existing paths. The new benchmark first passed a Dry job.

## Reproduction

Run these three existing methods on each checkout, sequentially:

```powershell
dotnet run --project benchmarks/Kevlar.Benchmarks -c Release -- --filter '*CircuitBreakerBenchmarks.Kevlar_RatioClosedHappyPath' '*CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured' '*CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured' --artifacts <unique-directory>
```

For the candidate, also include `*CircuitBreakerBenchmarks.Kevlar_SlowCallDetectionConfigured`.
The default job selects its own warmup and measurement counts. All results above use the same
runtime, machine, benchmark source for the existing cases, and job settings. Earlier Short jobs
were exploratory and are not used in this table.

Measurement phase logs: baseline 14:53:06–14:54:35 UTC; candidate 14:55:01–14:57:57 UTC;
control 14:58:19–15:00:14 UTC. The reservation was verified after the control completed.
