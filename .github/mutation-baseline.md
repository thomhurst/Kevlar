# Mutation baseline

The baseline was measured on 21 August 2026 with `dotnet-stryker` 4.16.0, the Release `net10.0` build, and all 289 unit tests. Of 435 score-eligible core-strategy mutants, 304 were killed, 19 timed out, 101 survived, and 11 had no coverage, producing a 74.25% score. Stryker also classified 154 mutants as compile errors and 490 as ignored; neither status enters the score. CI fails below 74%.

Within the configured `Strategies/**/*.cs` scope, no mutation category or source file is excluded by repository configuration. The HTML and JSON artifacts retain every survivor for review. The initial survivors fall into these audited groups:

- Description and validation-message string mutations. These do not change resilience behavior; exact message/description contracts are expanded under issues #17 and #19.
- Synchronous-completion versus awaited-path mutations whose branches produce the same `Outcome<T>`. These are equivalent for the exercised completed operation; callback-failure and asynchronous-branch contracts are expanded under issues #20 and #21.
- Concurrency and timing mutations in circuit breaking, hedging, rate limiting, concurrency limiting, retry, and timeout. Mutants that deadlock are bounded and reported as timeouts. Deterministic race coverage is tracked by issues #10 and #13-#24; model/property invariants are tracked by issue #31.
- Remaining arithmetic and state-transition survivors are owned by the focused strategy issues: circuit breaker #22 and #24, fallback #20, hedging #14, rate limiting #15, concurrency limiting #23, retry #19 and #21, and timeout #13. Cross-strategy model invariants are owned by #31. Their exact lines remain visible in the report rather than being suppressed, and the 74% ratchet prevents the aggregate baseline from regressing while those suites land.

Raise the checked-in threshold when new tests improve the measured score. A new behavior-sensitive survivor must be killed or documented here with its owning follow-up issue; do not add broad `ignore-mutations` entries.
