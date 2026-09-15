```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  Categories=PrimaryWins  

```
| Method            | Mean     | Error    | StdDev  | Ratio | Allocated | Alloc Ratio |
|------------------ |---------:|---------:|--------:|------:|----------:|------------:|
| KevlarPrimaryWins | 203.9 ns | 12.33 ns | 0.68 ns |  1.00 |         - |          NA |
