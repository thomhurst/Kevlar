---
sidebar_position: 29
---

# Support policy

## Target frameworks and compatibility

Removing a target framework from a stable package is a breaking change and happens only in a new
major version. Kevlar may add target frameworks in a minor release when doing so does not change
the behavior or dependency graph of existing targets. A framework reaching end of support from its
vendor does not by itself remove that target from an existing Kevlar major line.

Public API removals also require a new major version. When a safe migration exists, an API is
marked `[Obsolete]` for at least one minor release before it is removed. Security, legal, or
platform constraints may require faster action; release notes will identify any such exception and
its migration path.

Behavioral fixes can ship in patch releases when they restore documented intent. Minor releases
may add compatible APIs and diagnostics. Release notes call out changes that could affect unusual
or implementation-dependent usage.

## Minimum dependency versions

Dependency versions are managed in
[`Directory.Packages.props`](https://github.com/thomhurst/Kevlar/blob/main/Directory.Packages.props).
For a released version, use the dependency groups on that version's NuGet package page; the main
branch can contain updates that have not shipped yet. Test-only and build-only dependencies in the
central file are not part of the shipped package contract.

These are minimum versions, not forced runtime versions: NuGet can select a later compatible
version requested by the application. CI checks the packed dependency versions against the central
file and builds and runs package consumers on .NET 8 and .NET 10, with downgrade and dependency
constraint warnings treated as errors.

For .NET 8 and .NET Standard assets, Microsoft.Extensions packages, Microsoft.Bcl.AsyncInterfaces,
Microsoft.Bcl.TimeProvider, and System.Threading.RateLimiting keep minimum versions on the 8.x
line or earlier. This lets applications use an 8.x dependency stack without upgrading those
packages to a newer major. CI checks both the shipped dependency floors and the resolved .NET 8
and .NET Standard consumer graphs, including transitive dependencies. The .NET 10 consumer also
tests newer versions.

Renovate can update these compatibility dependencies within the supported line. Dependencies
already on newer lines for tests and tooling can continue updating independently. gRPC and
Reservoir use their own version schemes and are not subject to the Microsoft package version cap.

Renovate updates interdependent gRPC packages together. Dependency updates do not require a
separate documentation version edit. Raising a dependency floor that changes compatibility must
be called out in release notes.

## Kevlar package lockstep

Packages that consume Kevlar internals must use the same version as `Kevlar`. NuGet exact-version
dependencies enforce this for `Kevlar.Testing`, `Kevlar.Extensions.Http`,
`Kevlar.Extensions.RateLimiting`, `Kevlar.Extensions.DependencyInjection`, and
`Kevlar.Extensions.Logging`. `Kevlar.Extensions.Grpc` is also coupled through dependency injection,
so it exact-pins both `Kevlar` and `Kevlar.Extensions.DependencyInjection`.

Upgrade these packages together. If package versions are managed centrally, assign one version to
the coupled package set. A satellite-first upgrade can raise the package-downgrade warning
`NU1605`; other version skew can raise the dependency-constraint warning `NU1608`. Depending on
client settings, restore can still succeed and allow a mixed-version deployment that fails at
runtime. To reject version skew, treat both warnings as errors in the consuming project or a
shared build properties file:

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);NU1605;NU1608</WarningsAsErrors>
</PropertyGroup>
```

`Kevlar.Chaos` uses only public APIs and is not part of this lockstep set.

## Security support

Security fixes target the latest stable minor release in the current major line. After a new major
version reaches general availability, the previous major continues receiving security fixes for
six months. Older minors in a supported major must upgrade to that major's latest minor to receive
the fix. Pre-release builds are not supported. Report vulnerabilities through GitHub's
[private security advisory form](https://github.com/thomhurst/Kevlar/security/advisories/new).

## Release cadence

Patch releases ship as needed for correctness and security. While a major line is active, minor
releases are targeted roughly monthly; this is a planning target, not a service-level agreement.
Major releases are intentionally rare and include migration guidance for breaking changes.

## Maintenance and roadmap

Kevlar is currently maintained by one primary maintainer, [@thomhurst](https://github.com/thomhurst).
`CODEOWNERS` routes repository changes for review but does not provide maintainer redundancy or a
response-time guarantee. Organizations that require multi-maintainer governance should account for
that bus-factor risk in adoption decisions.

The public issue tracker and GitHub milestones are the roadmap. An issue or milestone expresses
intent, not a delivery commitment; released packages and this support policy are the authoritative
compatibility contract.
