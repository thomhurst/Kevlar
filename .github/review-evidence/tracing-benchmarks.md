# Tracing adapter overhead

The same retry happy path runs without and with a global `KevlarTracing.Listen()` subscription.
This measures the new opt-in adapter, not a core execution-path change. Both activity cases
include identical activity creation/disposal so events cannot accumulate between invocations.
`NoActivity` uses a completed `ValueTask<int>`; activity cases use synchronous execution.
Compare registered/unregistered rows within each method, not different methods.

Measured from implementation commit `636698061f9691e6a3c50e97440374451148d296` on
2026-09-26, 13:03:01–13:06:57 UTC. The shared Redis performance reservation was held
through preparation and measurement, with ownership verified before and after. No other
owned heavy workload ran alongside the measurements. This is a local diagnostic measurement;
it is not a network throughput estimate or proof of runner-isolated performance acceptance.
The sampled, unregistered baseline showed higher variance; retain the reported error/stddev.

BenchmarkDotNet Dry validation and a representative default-job probe completed first.
The final command was:

```text
dotnet run --project benchmarks/Kevlar.Benchmarks -c Release --no-build -- --filter "*TracingBenchmarks*" --artifacts artifacts/tracing-final
```

Registration adds about 45 ns with no activity or an unsampled activity, with no additional
allocations. A sampled attempt adds about 346 ns and 912 B for its activity event and tags.
The core remains unchanged when the adapter is not registered.

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method            | Registered | Mean      | Error    | StdDev   | Gen0   | Allocated |
|------------------ |----------- |----------:|---------:|---------:|-------:|----------:|
| **NoActivity**        | **False**      |  **71.28 ns** | **1.365 ns** | **1.277 ns** |      **-** |         **-** |
| UnsampledActivity | False      | 155.05 ns | 2.886 ns | 6.025 ns | 0.0317 |     416 B |
| SampledActivity   | False      | 148.96 ns | 3.485 ns | 9.828 ns | 0.0317 |     416 B |
| **NoActivity**        | **True**       | **116.13 ns** | **1.170 ns** | **0.977 ns** |      **-** |         **-** |
| UnsampledActivity | True       | 199.68 ns | 3.951 ns | 6.601 ns | 0.0317 |     416 B |
| SampledActivity   | True       | 494.57 ns | 6.586 ns | 5.499 ns | 0.1011 |    1328 B |
