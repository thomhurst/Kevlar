# Timeout timer scheduling experiment

Related to #512. Performance acceptance is pending; this report does not claim an improvement.

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

Local validation is deferred while another agent holds the shared performance
reservation. Linux/Windows CI, allocation gates, a fresh contention trace, and
pinned baseline/candidate/baseline stress and microbenchmarks must pass before
this candidate can be accepted.
