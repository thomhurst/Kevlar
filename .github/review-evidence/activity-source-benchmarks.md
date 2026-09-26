# Built-in ActivitySource measurements

These local diagnostic measurements compare the existing empty, retry, and hedge hot paths
before and after built-in tracing. No ActivityListener is registered for the before/after cases.
All four existing cases retain **0 B per operation**. Listener checks have a small fixed CPU cost; these results
do not claim zero elapsed-time overhead.

Baseline commit: `2ef9639f652692e06a9ff939902d4d32870a2105`.
Candidate commit: `c450082727daa4a7678ede281b1e1bc587ffed4a`, with the final retry refinement
measured at `157108bc` (avoids duplicate retry timing events already represented by attempt spans).
The intervening main updates added adapter/friend-assembly and health-inspection code, without
changing the benchmarked empty/retry/hedge execution paths.

The shared Redis performance reservation was held through preparation and all measurements,
with ownership checked before and after measurement phases. No other owned heavy workload ran
alongside benchmarks. The initial baseline ran at 13:38:07–13:39:46 UTC on 2026-09-26; the final
candidate group ran at 14:00:06–14:01:49 and baseline control at 14:02:07–14:03:48 UTC.
Earlier intermediate candidates were used to remove continuation copying and keep tracing-only
state construction out of the disabled path. An initial unprotected baseline worktree was removed
by concurrent cleanup; that incomplete run was discarded. The successful baseline used a locked
worktree with reports outside the worktree.

| Existing benchmark | Initial baseline (ns) | Baseline control (ns) | Candidate (ns) | Baseline/candidate throughput (million ops/s) | Allocation |
| --- | ---: | ---: | ---: | ---: | ---: |
| Empty async | 8.403 | 8.684 | 8.538 | 115.15 / 117.12 | 0 B / 0 B |
| Empty sync | 4.604 | 4.799 | 5.916 | 208.38 / 169.03 | 0 B / 0 B |
| Retry happy path | 69.57 | 70.08 | 74.25 | 14.27 / 13.47 | 0 B / 0 B |
| Hedge primary wins | 196.2 | 202.9 | 198.1 | 4.93 / 5.05 | 0 B / 0 B |

Compared with the final control, the observed increases are approximately 1.1 ns for an empty
synchronous call and 4.2 ns for retry (23% and 6% on these very short synthetic operations).
Intermediate default-job results ranged from 4.75–5.92 ns for candidate empty sync and 72.16–77.52 ns
for candidate retry; separate baseline controls also varied. Preserve the uncertainty below and
measure application workloads before drawing throughput conclusions. This workstation experiment
is not proof of runner-isolated performance acceptance.

Commands (each filter group was run sequentially, without other owned heavy work):

```text
dotnet run --project benchmarks/Kevlar.Benchmarks -c Release --no-build -- --filter "*OverheadBenchmarks.Kevlar_Empty" "*OverheadBenchmarks.Kevlar_EmptySync" "*RetryBenchmarks.Kevlar_HappyPath" "*HedgingBenchmarks.KevlarPrimaryWins" --artifacts <report-directory>
dotnet run --project benchmarks/Kevlar.Benchmarks -c Release --no-build -- --filter "*RetryBenchmarks.Kevlar_HappyPath" --artifacts <final-retry-directory>
```

## Final disabled-path reports

The empty and hedge reports use `c4500827`; the retry report uses `157108bc`.

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method           | Categories | Mean     | Error     | StdDev    | Ratio | Allocated | Alloc Ratio |
|----------------- |----------- |---------:|----------:|----------:|------:|----------:|------------:|
| Kevlar_Empty     | Empty      | 8.538 ns | 0.0944 ns | 0.0837 ns |  1.00 |         - |          NA |
|                  |            |          |           |           |       |           |             |
| Kevlar_EmptySync | EmptySync  | 5.916 ns | 0.0295 ns | 0.0230 ns |  1.00 |         - |          NA |

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Categories=PrimaryWins  

```
| Method            | Mean     | Error   | StdDev  | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------ |---------:|--------:|--------:|------:|--------:|----------:|------------:|
| KevlarPrimaryWins | 198.1 ns | 3.45 ns | 3.06 ns |  1.00 |    0.02 |         - |          NA |

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Categories=HappyPath  

```
| Method           | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| Kevlar_HappyPath | 74.25 ns | 1.226 ns | 1.087 ns |  1.00 |    0.02 |         - |          NA |


## Baseline control reports

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method           | Categories | Mean     | Error     | StdDev    | Ratio | Allocated | Alloc Ratio |
|----------------- |----------- |---------:|----------:|----------:|------:|----------:|------------:|
| Kevlar_Empty     | Empty      | 8.684 ns | 0.0301 ns | 0.0235 ns |  1.00 |         - |          NA |
|                  |            |          |           |           |       |           |             |
| Kevlar_EmptySync | EmptySync  | 4.799 ns | 0.0114 ns | 0.0089 ns |  1.00 |         - |          NA |

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Categories=PrimaryWins  

```
| Method            | Mean     | Error   | StdDev  | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------ |---------:|--------:|--------:|------:|--------:|----------:|------------:|
| KevlarPrimaryWins | 202.9 ns | 3.71 ns | 3.47 ns |  1.00 |    0.02 |         - |          NA |

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Categories=HappyPath  

```
| Method           | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|----------------- |---------:|---------:|---------:|------:|----------:|------------:|
| Kevlar_HappyPath | 70.08 ns | 0.497 ns | 0.441 ns |  1.00 |         - |          NA |

## Enabled tracing

Permanent coverage benchmarks compare the same named shield with and without an
`ActivityListener` sampling `AllDataAndRecorded`. They retain no exported spans. Span creation,
tags, events, and disposal are included. Eight Dry cases and a representative default-job probe
completed before the final eight-case run at 14:07:09–14:10:04 UTC, using commit
`157108bc18ef21d8e61b605cd8953f34eabf041c`.

The permanent hedge case uses zero delay, so it also starts an immediate additional attempt.
Its 408 B without a listener are existing hedge scheduling allocations. The earlier
`HedgingBenchmarks.KevlarPrimaryWins` case uses a delayed hedge that the primary avoids and
remains allocation-free. Compare listener on/off within each method; the two hedge methods
exercise different scheduling paths.

```text
dotnet run --project benchmarks/Kevlar.Benchmarks -c Release --no-build -- --filter "*ActivitySourceBenchmarks*" --artifacts <report-directory>
```

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method           | ListenerEnabled | Mean         | Error      | StdDev     | Gen0   | Gen1   | Allocated |
|----------------- |---------------- |-------------:|-----------:|-----------:|-------:|-------:|----------:|
| **EmptyAsync**       | **False**           |     **8.847 ns** |  **0.1739 ns** |  **0.1627 ns** |      **-** |      **-** |         **-** |
| EmptySync        | False           |     6.026 ns |  0.1064 ns |  0.0995 ns |      - |      - |         - |
| RetryHappyPath   | False           |    75.946 ns |  0.6103 ns |  0.5097 ns |      - |      - |         - |
| HedgePrimaryWins | False           |   597.767 ns |  4.8728 ns |  4.3196 ns | 0.0305 |      - |     408 B |
| **EmptyAsync**       | **True**            |   **173.253 ns** |  **2.5831 ns** |  **2.2899 ns** | **0.0410** |      **-** |     **536 B** |
| EmptySync        | True            |   162.296 ns |  1.7862 ns |  1.5834 ns | 0.0410 |      - |     536 B |
| RetryHappyPath   | True            |   528.856 ns |  5.4485 ns |  4.8299 ns | 0.1040 |      - |    1360 B |
| HedgePrimaryWins | True            | 2,482.288 ns | 23.2803 ns | 21.7764 ns | 0.4044 | 0.0038 |    5320 B |
