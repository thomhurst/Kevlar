# Controlled outage measurements

Run an open-loop simulation of healthy, slowdown, outage, and recovery phases against an in-process asynchronous dependency:

```bash
dotnet run -c Release --project benchmarks/Kevlar.OutageTests -- --phase-seconds 30 --rate 100 --max-inflight 512
python .github/scripts/outage_docs.py --input artifacts/outage/outage-results.json --output artifacts/outage/outage-results.md
```

Requires .NET 10. Use `--phase-seconds 1` for a roughly 16-second smoke run across four compositions. The separate accounting tests verify percentile, recovery-window, scheduled-latency, and cleanup calculations. The workflow runs them before measuring.

Every logical request has a fixed scheduled arrival. Slow responses never reduce offered arrivals; scheduler lag and harness-cap rejections remain visible. Each composition starts with fresh strategy state, runs all four phases, and drains cancelled hedge attempts before the next composition. A 30-second drain allowance fails the run if work remains.

The simulator deliberately gives additional slowdown attempts a faster alternate replica. These results illustrate that configured behavior and do not establish production capacity or compare libraries. JSON records settings, runtime, OS, commit, phase boundaries, outcomes, amplification, nearest-rank percentiles, and cleanup. Published measurements must come from a committed build; local uncommitted runs are diagnostic only.
