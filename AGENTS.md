# Repository Guidelines

## Local workload coordination

Before local benchmarks, profiling, stress runs, builds, tests, restores, or other heavy work, follow [the shared performance lock workflow](scripts/PerformanceLock.md). Reserve the cross-repository Redis `performance` key through the canonical shared Dekaf checkout, configured by `SHARED_PERFORMANCE_LOCK_CHECKOUT` (default `C:/git/Dekaf`). Use the same checkout setting in every cooperating repository; this repository's item-lock backend is separate. Reading and editing can continue while another agent owns the reservation.

## Agent output locations

Logs, reports, PR drafts and other evidence, throwaway checkouts and downloaded tools each have one home outside the main checkout; never write loose files beside the repository. The locations are in [issue-pr-loop § Isolation and ownership](.agents/skills/issue-pr-loop/SKILL.md#isolation-and-ownership).

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

## Pull request reviews

- Resolve each PR review thread, whether a human or a bot opened it, as soon as you have dispositioned it: the fix is pushed to the PR head and your reply names the commit, or your reply pushes back on the finding with evidence. Leave a thread open only while it has no disposition. If the reviewer replies after your disposition, unresolve the thread and handle the reply.

## Autonomous self-improvement

Treat every problem you notice as work to act on, whether you find it while testing, developing, reading code, reviewing, or doing anything else. Examples include bugs, flaky or failing tests, incorrect docs, misleading comments, broken tooling, and gaps in these instructions.

- If the fix is trivial and low-risk, fix it as part of the current work and mention it in the PR description.
- If the fix is out of scope for the current work, raise a GitHub issue at the point you discover it so it can be fixed later. Include what you saw, where you saw it (file and line, command, or CI run), how to reproduce it, and the expected behavior. Search existing issues first, and add to a matching issue instead of opening a duplicate.
- Do not ignore a problem because it existed before your change or is unrelated to it.
