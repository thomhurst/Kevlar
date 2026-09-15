# Reservoir 1.9.1 validation

Baseline: 658453c8c26186527ff996bb73bc113d6e091d2b (Reservoir 1.4.0).
Candidate: 261c32caacf3b076d7f3d0c72d56feb16773452b (Reservoir 1.9.1 and explicit Destroy policies).

Windows 11, Intel Core i7-12700K, .NET SDK 10.0.401, .NET runtime 10.0.12, BenchmarkDotNet 0.15.8.
Sequential baseline and candidate runs used ShortRun (one launch, three warmup iterations, three measurement iterations), after Dry validation. Both ran under the shared performance reservation. No other owned heavy workload ran during measurements; unrelated Node processes were present. These local results are diagnostic only, not performance acceptance or statistical equivalence. There was no trailing baseline run or AMD validation.

| Benchmark | 1.4.0 mean ns/op | 1.9.1 mean ns/op | Change | Allocated before/after |
|---|---:|---:|---:|---:|
| HedgingBenchmarks.KevlarPrimaryWins | 203.95 | 195.73 | -4.03% | 0 / 0 B |
| OverheadBenchmarks.Kevlar_NestedEmptySync | 127.85 | 124.88 | -2.32% | 0 / 0 B |
| TimeoutBenchmarks.Kevlar_HappyPath | 129.44 | 121.10 | -6.45% | 0 / 0 B |

Command (run on each version, with separate artifact directories):

```powershell
dotnet run --project benchmarks/Kevlar.Benchmarks -c Release --no-build -- --filter '*OverheadBenchmarks.Kevlar_NestedEmptySync' '*TimeoutBenchmarks.Kevlar_HappyPath' '*HedgingBenchmarks.KevlarPrimaryWins' --job Short --exporters json --artifacts artifacts/reservoir-validation/<version>
```

Local validation:

- `dotnet build Kevlar.slnx -c Release`: passed with zero warnings and errors.
- `dotnet run --project tests/<project> -c Release [-f <framework>] --no-build -- --timeout 5m`: 3,668 tests passed across core, chaos, testing, rate-limiting, logging, allocation, integration, analyzer, observability, and compatibility suites.
- Core, chaos, testing, rate-limiting, logging, and allocation suites ran on both net8.0 and net10.0.
- `dotnet run --project tests/Kevlar.NetStandard.Tests -c Release -f net48 --no-build`: passed.
- Existing compiler and compatibility suites cover the newly required Destroy interface contract.

The old Reservoir 1.7.0 PR (#484) reported a possible AMD regression. This short Intel run does not resolve that finding.

Documentation: npm ci and npm run build passed. Playwright 1.63.0 with installed Chromium revision 1234 verified both changed pages and captured the PNGs. Shared performance ownership was verified before candidate measurements and before release.

