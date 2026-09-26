# Timeout timer scheduling experiment

Related to #512. Pinned comparisons establish improved multi-worker throughput,
with zero-allocation success paths and no measured default-path regression.

The traces retained in #560 and #570 identify runtime timer arming/reset as the
sampled Kevlar contention source. Four pool-placement experiments failed either
allocation or throughput acceptance. This design changes scheduling instead:
successful rentals retain an earlier pending timer wakeup and update the active
deadline under a source-local lock. A wakeup checks the current rental's deadline
and reschedules when that deadline is still in the future. A shorter deadline
updates the runtime timer immediately. Returning an inactive source does not
disable the pending timer; an idle wakeup stops without cancellation or rearming.

The pool retains one source per returning thread plus Reservoir's bounded shared
fallback. Overflow and cancelled sources dispose their timers. Timer creation
suppresses ExecutionContext flow so a retained timer does not retain the creating
caller's AsyncLocal state. Custom TimeProvider and .NET Standard behavior remain
on their existing paths.

Cancellation callbacks run outside the source lock. An expiry-selected source
cannot return to the pool until cancellation finishes; completion destroys it
instead of blocking on user callbacks. Upstream registrations are drained before
successful reuse. Every timer callback checks the current active deadline under
the lock, so an old queued wakeup cannot cancel a later rental early.

New regression coverage includes shorter and longer deadlines on actual reused
sources, upstream registration cleanup, concurrent expiry/completion and reuse,
thread migration, callback completion without deadlock, and ExecutionContext
isolation. Existing timeout, custom-provider, nesting, cancellation, and allocation
suites remain unchanged.

Local validation was deferred while another agent held the shared performance
reservation. Linux and Windows CI, allocation gates on .NET 8/10, deterministic
models, stress, outage measurements, and documentation checks pass for the
measured revision. Later evidence-only changes must pass fresh CI before merge.

## Pinned stress and contention evidence

[Run 36255879826](https://github.com/thomhurst/Kevlar/actions/runs/36255879826):
baseline `4e41e545ca87e21f3f249e26fb5fa87aa898ad4d`, candidate
`53d83205ca3f5a16ca9eb684057c4b01e6319db1`. Sequential two-minute unprofiled
baseline/candidate/baseline phases run on one Ubuntu 24.04 runner, .NET 10.0.12,
four logical processors, and identical fixture/settings revisions.

| Scenario | Workers | Baseline before ops/s | Candidate ops/s | Baseline after ops/s |
|---|---:|---:|---:|---:|
| Shared ratio pipeline | 1 | 3,436,561 | 3,296,550 | 3,222,958 |
| Shared ratio pipeline | 4 | 6,386,209 | 9,042,383 | 7,633,011 |
| Timeout and retry | 4 | 7,388,501 | 14,921,244 | 10,157,415 |
| Per-worker ratio pipeline | 4 | 6,602,043 | 10,230,416 | 7,881,220 |

The candidate beats both controls for every multi-worker scenario: shared ratio
throughput improves 18.5–41.6%, timeout/retry 46.9–102.0%, and per-worker ratio
29.8–55.0%. Single-worker throughput remains between controls. Control drift is
substantial, so the smaller gain against the stronger control is the conservative
comparison. Candidate stress allocation is 0.00004–0.00015 B/op, including
amortized harness costs; strict steady-state allocation gates remain zero.

The separate trace records 641 contention starts, zero lost events, and zero
unmatched starts/stops. One event (0.123 ms) is attributed to the Kevlar timeout
path; the other 640 are in Polly stacks. Unprofiled candidate contention counts
are 0/3/0/0 for the four Kevlar scenarios above, versus 0/240/278/169 before and
0/67/133/59 after. Trace counts are diagnostic; throughput comparisons use only
the unprofiled phases.

## Pinned microbenchmarks

[Run 36255881488](https://github.com/thomhurst/Kevlar/actions/runs/36255881488)
uses the same pinned revisions and identical fixtures, with sequential phases
on one Intel Xeon Platinum 8370C Ubuntu runner. BenchmarkDotNet 0.15.8,
.NET 10.0.12, default job, MemoryDiagnoser. Every warmed case allocates zero bytes.

| Method | Baseline before ns | Candidate ns | Baseline after ns |
|---|---:|---:|---:|
| Empty | 12.74 | 12.74 | 13.53 |
| TimeoutSuccess | 216.86 | 215.84 | 218.84 |
| NestedTimeoutSuccess | 412.42 | 388.97 | 410.53 |
| RetrySuccess | 133.97 | 120.56 | 121.53 |
| RetryHandledResult | 238.00 | 237.51 | 240.90 |
| HedgePrimaryWins | 348.68 | 344.61 | 345.35 |
| ExplicitChild | 386.19 | 369.23 | 383.79 |

The direct timeout improves 0.5–1.4%, nested timeouts 5.3–5.7%, and explicit child
execution 3.8–4.4%. Changes in paths that do not execute a timeout should not be
attributed to this implementation. No case is slower than both controls. These
measurements establish acceptance for these fixtures, not universal production
latency or scalability guarantees.
