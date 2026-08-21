# Repository Guidelines

## Project Structure & Module Organization

`Kevlar.slnx` groups the .NET projects. Core APIs live in `src/Kevlar`; HTTP and dependency-injection adapters have sibling projects under `src/`, and Roslyn diagnostics live in `src/Kevlar.Analyzers`. Unit, integration, and analyzer tests have matching projects under `tests/`. Benchmarks live in `benchmarks/Kevlar.Benchmarks`. Docusaurus content is under `docs/`: prose in `docs/docs`, React pages in `docs/src`, and assets in `docs/static`.

## Build, Test, and Development Commands

- `dotnet build Kevlar.slnx -c Release` builds every library, test project, and benchmark with warnings treated as errors.
- `dotnet run --project tests/Kevlar.Tests -c Release --no-build -- --timeout 5m` runs the TUnit unit suite after a Release build.
- `dotnet run --project tests/Kevlar.IntegrationTests -c Release --no-build -- --timeout 5m` runs HTTP, database, and messaging integration tests.
- `dotnet run --project tests/Kevlar.Analyzers.Tests -c Release --no-build -- --timeout 5m` validates analyzer diagnostics.
- `dotnet run -c Release --project benchmarks/Kevlar.Benchmarks` runs BenchmarkDotNet for performance-sensitive changes.
- From `docs/`, run `npm ci`, then `npm start` for local authoring or `npm run build` to validate the site.

## Coding Style & Naming Conventions

Use four-space indentation, file-scoped namespaces, braces on new lines, and modern C# patterns. Public types and members use `PascalCase`; locals and parameters use `camelCase`; private fields use `_camelCase`. Name one primary type per file. Keep hot paths zero-allocation where practical; benchmark unavoidable allocation changes. Nullable reference types and implicit usings are enabled, `LangVersion` is `latest`, and warnings fail the build. Document public APIs with XML comments. No repository-specific formatter is configured; match nearby code and keep `dotnet build` clean.

## Testing Guidelines

Tests use TUnit with `[Test]` methods and names such as `Exponential_Respects_MaxDelay`. Put focused behavior tests in `Kevlar.Tests`, cross-component scenarios in `Kevlar.IntegrationTests`, and diagnostic cases in `Kevlar.Analyzers.Tests`. Add regression tests for bug fixes and edge cases. No fixed coverage threshold is configured; meaningful behavioral coverage is expected.

## Commit & Pull Request Guidelines

History follows Conventional Commit-style subjects, often scoped: `feat:`, `fix(timeout):`, `perf(fallback):`, `docs:`, and `chore(agents):`. Keep subjects imperative and focused. Pull requests should explain behavior and motivation, link relevant issues, and list validation commands. Include before/after benchmark results for hot-path changes and screenshots for visible docs changes. Ensure Linux and Windows CI builds and tests pass before merge.

## Configuration & Dependencies

Manage NuGet versions centrally in `Directory.Packages.props`; avoid project-local version drift. Never commit credentials or NuGet tokens—publishing uses GitHub Actions trusted publishing.
