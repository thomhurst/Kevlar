# Repository Guidelines

## Local workload coordination

Before local benchmarks, profiling, stress runs, builds, tests, restores, or other heavy work, follow [the shared performance lock workflow](scripts/PerformanceLock.md). All four repositories reserve the same Redis `performance` key through `C:/git/Dekaf/scripts/AgentLocks.ps1`; this repository's item-lock backend is separate. Reading and editing can continue while another agent owns the reservation.

## Validation

- Build: `dotnet build Kevlar.slnx -c Release`.
- TUnit tests: `dotnet run --project tests/<project> -c Release --no-build -- --timeout 5m`. For multi-targeted projects such as `Kevlar.Tests`, add `-f net8.0` or `-f net10.0` before `--no-build`.
- Docs: run `npm ci`, then `npm run build` in `docs/`.
- See `.github/workflows/ci.yml` for the full test matrix, coverage/allocation gates, and documentation checks. Linux and Windows CI must pass before merge.

## Project conventions

- Match nearby C# style and document public APIs with XML comments.
- Manage NuGet versions in `Directory.Packages.props`.
- Add regression tests for bug fixes and edge cases in the matching `tests/` project.
- Keep hot paths zero-allocation where practical. Benchmark hot-path changes with `benchmarks/Kevlar.Benchmarks` and include before/after results in the PR.
- In C# examples (README, docs, XML comments), the first shorthand-strategy argument may be positional when the method name makes its meaning clear. Name every later numeric, duration, or boolean argument. Always name both `CircuitBreaker` arguments: `consecutiveFailures:` and `breakDuration:`.
- Use Conventional Commit subjects. Include screenshots for visible docs changes.
