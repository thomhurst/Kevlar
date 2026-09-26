---
sidebar_label: Outage and recovery
---

# Controlled outage and recovery

The controlled outage harness schedules requests independently of earlier completions through
healthy, slowdown, outage, and recovery phases. It records p95/p99 latency, success and failure
counts, admission and rejection counts, downstream amplification, recovery time, and hedge cleanup.

The first measured report is pending validation of this new harness. No performance result is
claimed on this page yet. The [outage workflow](https://github.com/thomhurst/Kevlar/actions/workflows/outage.yml)
will publish JSON and Markdown artifacts; the reviewed report will replace this placeholder before merge.

```bash
dotnet run -c Release --project benchmarks/Kevlar.OutageTests -- --phase-seconds 30 --rate 100 --max-inflight 512
```

PR smoke runs use one second per phase. Scheduled and manual runs use 30 seconds per phase.
Shared-runner latency is descriptive and never used as a pass/fail threshold.
