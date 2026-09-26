# Retain Reservoir as the core object pool

Status: accepted for the current package line.

This records the keep-dependency resolution of [#521](https://github.com/thomhurst/Kevlar/issues/521).
It supersedes the vendoring proposal in [#344](https://github.com/thomhurst/Kevlar/issues/344);
closing that issue did not mean its proposed implementation shipped.

## Decision and rationale

Keep Reservoir as an explicit runtime dependency on every core target framework. The current
range is `[1.9.1, 2.0.0)`, declared in `Directory.Packages.props` and consumed by
`src/Kevlar/Kevlar.csproj`.

Kevlar uses Reservoir for execution contexts, additional-attempt state, hedging, and timeout
state. Retaining the existing pools preserves their allocation and concurrency behavior without
introducing a second pool implementation to maintain. [#497](https://github.com/thomhurst/Kevlar/pull/497)
explicitly upgraded Reservoir to 1.9.1 and validated the integration after #344 closed.
Vendoring remains a possible future change, but requires its own concurrency tests, allocation
gates, and before/after benchmarks; a smaller dependency graph alone does not establish that
an alternative pool preserves the current behavior.

## Consequences

- The core is not dependency-free. [README](../../README.md#requirements-targets-and-support) lists Reservoir and
  the additional BCL compatibility dependencies used by the `netstandard2.0` asset.
- The existing unsigned assembly policy remains in place. See the
  [library-author guidance](../docs/library-authors.md#assembly-identity-and-strong-naming) and
  [#271](https://github.com/thomhurst/Kevlar/pull/271) for the strong-naming limitation and
  migration boundary.
- `scripts/Verify-Packages.ps1` continues to require Reservoir in every core dependency group.
  No runtime code, package reference, version range, allocation gate, or benchmark changes
  accompany this decision.
