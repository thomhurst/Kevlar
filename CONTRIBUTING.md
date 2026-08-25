# Contributing to Kevlar

Thanks for helping improve Kevlar. By participating, you agree to follow the
[Code of Conduct](CODE_OF_CONDUCT.md).

## Before opening a change

- Search existing issues and pull requests.
- Use a bug report for incorrect behavior, a feature request for a new contract, and a question
  for usage help.
- For security vulnerabilities, follow [SECURITY.md](SECURITY.md) instead of opening a public
  issue.
- Keep one behavior change per pull request. Link the issue it resolves.

## Local setup

Install the .NET 8 and .NET 10 SDKs. Documentation changes also need Node.js 24 and npm.

```powershell
git clone https://github.com/thomhurst/Kevlar.git
cd Kevlar
dotnet build Kevlar.slnx -c Release
```

The build treats warnings as errors. Match the existing C# style: four-space indentation,
file-scoped namespaces, braces on new lines, nullable annotations, and one primary type per file.
Document public APIs with XML comments and update the matching `PublicAPI.Unshipped.txt` file.

## Tests

Tests use TUnit. Put focused behavior tests in `Kevlar.Tests`, cross-component scenarios in
`Kevlar.IntegrationTests`, and analyzer cases in `Kevlar.Analyzers.Tests`. Bug fixes need a
regression test that fails without the fix.

```powershell
dotnet build Kevlar.slnx -c Release
dotnet run --project tests/Kevlar.Tests -c Release -f net10.0 --no-build -- --timeout 5m
dotnet run --project tests/Kevlar.IntegrationTests -c Release --no-build -- --timeout 5m
dotnet run --project tests/Kevlar.Analyzers.Tests -c Release --no-build -- --timeout 5m
```

Run other project-specific suites when their package changes. CI builds on Windows and Linux,
enforces discovered-test floors, runs deterministic model and stress checks for core changes, and
publishes coverage artifacts. See `.github/workflows/ci.yml` for the authoritative matrix.

## Performance changes

Keep hot paths allocation-conscious. For a performance-sensitive change, add or update a
BenchmarkDotNet benchmark under `benchmarks/Kevlar.Benchmarks` and include before/after results:

```powershell
dotnet run -c Release --project benchmarks/Kevlar.Benchmarks -- --filter *RelevantBenchmark*
```

Explain the workload, runtime, operating system, CPU, and whether results are means, ratios, or
allocations.

## Documentation

User documentation lives under `docs/docs`; navigation is explicit in `docs/sidebars.ts`. Every C#
fence in README and the documentation is compiled unless an adjacent `doc-test-ignore` comment
gives a concrete reason.

```powershell
Push-Location docs
npm ci
Pop-Location
./scripts/Verify-Repo.ps1
./scripts/Verify-Docs.ps1
Push-Location docs
npm run build
Pop-Location
```

Package-consuming snippet checks require locally packed packages and a unique version:

```powershell
dotnet pack Kevlar.slnx -c Release -p:Version=0.0.0-local
./scripts/Verify-DocSnippets.ps1 -PackagesPath artifacts/package/release -Version 0.0.0-local
```

## Pull requests

A pull request should:

- explain behavior and motivation;
- link the issue it closes;
- list exact validation commands;
- include benchmark results for hot-path changes;
- include screenshots for visible site changes; and
- keep Linux and Windows CI green.

Maintainers may ask for a smaller scope or additional compatibility coverage. Contributions are
licensed under the repository's [MIT License](LICENSE).
