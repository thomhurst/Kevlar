# Contention report

Build the tool and capture runtime contention with managed stacks:

```sh
dotnet build tools/Kevlar.ContentionReport -c Release
dotnet-trace collect --providers 'Microsoft-Windows-DotNETRuntime:0x4000:5' --output contention.nettrace -- dotnet app.dll
dotnet run --project tools/Kevlar.ContentionReport -c Release --no-build -- contention.nettrace > stacks.json
```

The report pairs contention start/stop events by process and thread, groups the start
stack, and sums stop-event durations in milliseconds. Inspect `EventsLost`,
`UnmatchedStarts`, and `UnmatchedStops` before attributing costs. Contention events
report blocked lock acquisition, not all spin time or every cause of poor scaling.
A trace of the stress harness includes both libraries, warmup, and harness activity;
filter product frames carefully instead of treating every stack containing
`Kevlar.StressTests` as a Kevlar product contention.

The `Contention profile` workflow preserves the trace, report, runtime information,
and commit identity. Its optional `baseline_sha` runs unprofiled two-minute stress
phases in baseline/candidate/baseline order on one runner. Use those phases for
throughput/CPU comparisons; use the separate trace for attribution. Compare both
controls and report drift. Allocation acceptance comes from the unchanged allocation
gates, because stress results include amortized harness overhead.
