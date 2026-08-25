---
sidebar_position: 21
---

# Versioning and support policy

Kevlar follows [Semantic Versioning](https://semver.org/). A stable major version keeps source and
binary compatibility for documented public APIs. New features arrive in minor releases; compatible
fixes arrive in patches. A breaking change requires a new major version and migration notes in the
[changelog](https://github.com/thomhurst/Kevlar/blob/main/CHANGELOG.md).

The checked-in `PublicAPI.Shipped.txt` files are the API compatibility contract. CI rejects an
unreviewed public surface change. Documentation, behavior, exception types, metrics, and supported
target frameworks are also compatibility concerns even when a signature does not change.

## Package targets

<!-- supported-tfms:start -->
| Package | Target frameworks |
|---|---|
| `Kevlar` | `netstandard2.0`, `net8.0`, `net10.0` |
| `Kevlar.Analyzers` | `netstandard2.0` |
| `Kevlar.Chaos` | `netstandard2.0`, `net8.0`, `net10.0` |
| `Kevlar.Extensions.DependencyInjection` | `netstandard2.0`, `net8.0`, `net10.0` |
| `Kevlar.Extensions.Grpc` | `netstandard2.0`, `netstandard2.1`, `net8.0`, `net10.0` |
| `Kevlar.Extensions.Http` | `netstandard2.0`, `net8.0`, `net10.0` |
| `Kevlar.Extensions.RateLimiting` | `netstandard2.0`, `net8.0`, `net10.0` |
| `Kevlar.Testing` | `netstandard2.0`, `net8.0`, `net10.0` |
<!-- supported-tfms:end -->

`netstandard2.0` is the broad compatibility asset; `netstandard2.1` exists only where modern gRPC
contracts require it. Native .NET assets receive runtime-specific optimizations and telemetry.
Kevlar tests the compatibility assets from .NET 10 and tests the core packages directly on .NET 8
and .NET 10.

## Runtime lifecycle

Kevlar supports a native target while its corresponding Microsoft runtime is supported. According
to Microsoft's official [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core):

| Runtime | Track | Microsoft end of support | Kevlar policy |
|---|---|---|---|
| .NET 8 | LTS | November 10, 2026 | Supported through that date; upgrade to a supported runtime afterward |
| .NET 10 | LTS | November 14, 2028 | Supported through that date |

Consumers must stay on the latest runtime patch to remain within Microsoft's support policy.
Selecting a `netstandard` compatibility asset does not extend the support lifetime of the runtime
hosting the application. After a runtime reaches end of support, upgrade the runtime or retarget a
separately supported platform.
Kevlar may keep an out-of-support TFM temporarily for migration, but no security or compatibility
promise applies after its published end date.

## Release and breaking-change policy

- Security fixes target the latest stable minor in the current major line.
- Deprecations are documented before removal when a safe transition is possible.
- Breaking signatures, defaults, exception contracts, or package requirements are announced in the
  changelog and release notes with replacement guidance.
- Preview packages may change without a compatibility promise.
- Dependency minimums stay as low as practical; package verification compiles a clean consumer
  against the shipped floors.

Report compatibility regressions with the package, Kevlar version, target framework, and a minimal
reproduction. Report vulnerabilities privately under the
[private security advisory form](https://github.com/thomhurst/Kevlar/security/advisories/new).
