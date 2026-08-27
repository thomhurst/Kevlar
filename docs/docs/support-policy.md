---
sidebar_position: 29
---

# Support policy

## Minimum dependency versions

Kevlar keeps shipped dependency floors compatible with .NET 8-era applications. These are minimum
versions, not forced runtime versions: NuGet can select a later compatible version requested by the
application. Test-only and build-only dependencies are not part of this package contract.

| Dependency | Minimum version | Shipped by |
|---|---:|---|
| `Microsoft.Bcl.AsyncInterfaces` | `8.0.0` | `Kevlar` |
| `Microsoft.Bcl.TimeProvider` | `8.0.1` | `Kevlar`, `Kevlar.Chaos` |
| `Microsoft.Extensions.Configuration.Abstractions` | `8.0.0` | `Kevlar.Extensions.DependencyInjection`, `Kevlar.Extensions.Http` |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | `8.0.2` | `Kevlar.Extensions.DependencyInjection` |
| `Microsoft.Extensions.Http` | `8.0.1` | `Kevlar.Extensions.Http` |
| `Microsoft.Extensions.Logging` | `8.0.1` | `Kevlar.Extensions.Logging` |
| `Microsoft.Extensions.Logging.Abstractions` | `8.0.3` | `Kevlar.Extensions.Logging` |
| `Microsoft.Extensions.Options` | `8.0.2` | `Kevlar.Extensions.DependencyInjection` |
| `Microsoft.Extensions.Primitives` | `8.0.0` | `Kevlar.Extensions.DependencyInjection`, `Kevlar.Extensions.Http` |
| `Microsoft.Extensions.TimeProvider.Testing` | `8.0.0` | `Kevlar.Testing` |
| `System.Threading.RateLimiting` | `8.0.0` | `Kevlar.Extensions.RateLimiting` |
| `System.Threading.Tasks.Extensions` | `4.6.3` | `Kevlar`, `Kevlar.Chaos` |
| `System.Runtime.CompilerServices.Unsafe` | `6.1.2` | `Kevlar.Chaos` |
| `Grpc.Core.Api` | `2.83.0` | `Kevlar.Extensions.Grpc` |
| `Grpc.Net.ClientFactory` | `2.83.0` | `Kevlar.Extensions.Grpc` |
| `Reservoir` | `[1.4.0, 2.0.0)` | `Kevlar` |

Package verification rejects accidental dependency floors above major version 8, except for the
documented gRPC and Reservoir version lines. Raising a floor is a compatibility decision and must
be called out in release notes.

## Kevlar package lockstep

Packages that consume Kevlar internals must use the same version as `Kevlar`. NuGet exact-version
dependencies enforce this for `Kevlar.Testing`, `Kevlar.Extensions.Http`,
`Kevlar.Extensions.RateLimiting`, `Kevlar.Extensions.DependencyInjection`, and
`Kevlar.Extensions.Logging`. `Kevlar.Extensions.Grpc` is also coupled through dependency injection,
so it exact-pins both `Kevlar` and `Kevlar.Extensions.DependencyInjection`.

Upgrade these packages together. If package versions are managed centrally, assign one version to
the coupled package set. A partial upgrade fails restore with `NU1608` instead of allowing a mixed
version deployment that could fail at runtime. `Kevlar.Chaos` uses only public APIs and is not part
of this lockstep set.
