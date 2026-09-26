# Server interceptor overhead

Local BenchmarkDotNet default-job comparison, under the shared performance reservation. `Direct` is the previous behavior: invoking the same cached completed handler with no interceptor. The other cases add the new server interceptor, shared concurrency admission, or a warmed method partition. These are in-memory handler overhead measurements, not network latency or server load-test results. Existing client execution code is unchanged.

The benchmark passed a Dry run and an EmptyShield default-job probe before this four-case measurement. No other owned heavy workload ran during measurement. Shared-runner and machine effects still limit comparisons with other runs.

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method           | Mean        | Error     | StdDev    | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------- |------------:|----------:|----------:|-------:|--------:|-------:|----------:|------------:|
| Direct           |   0.8182 ns | 0.0341 ns | 0.0302 ns |   1.00 |    0.05 |      - |         - |          NA |
| EmptyShield      |  20.7824 ns | 0.4479 ns | 0.8191 ns |  25.43 |    1.32 | 0.0055 |      72 B |          NA |
| ConcurrencyLimit | 123.6426 ns | 0.3247 ns | 0.2711 ns | 151.30 |    5.22 | 0.0055 |      72 B |          NA |
| MethodPartition  | 139.9785 ns | 0.4278 ns | 0.3572 ns | 171.29 |    5.91 | 0.0055 |      72 B |          NA |

Final benchmark interval (UTC): 2026-09-26 12:36:06 to 2026-09-26 12:38:10. The shared performance reservation was held continuously through this interval.
