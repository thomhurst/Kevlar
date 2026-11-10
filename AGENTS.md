# Repository Guidelines

## Local workload coordination

Before local benchmarks, profiling, stress runs, builds, tests, restores, or other heavy work, follow [the local performance lock workflow](scripts/PerformanceLock.md). Reserve the Redis `performance` lock with this repository's `scripts/AgentLocks.ps1`; it coordinates Kevlar agents only, since the other repositories no longer share a cross-repository lock. Reading and editing can continue while another agent owns the reservation.

## Agent output locations

Logs, reports, PR drafts and other evidence, throwaway checkouts and downloaded tools each have one home outside the main checkout; never write loose files beside the repository. The locations are in [issue-pr-loop § Agent output locations](.agents/skills/issue-pr-loop/references/repository.md#agent-output-locations).

## Validation

- Build: `dotnet build Kevlar.slnx -c Release`.
- TUnit tests: `dotnet run --project tests/<project> -c Release --no-build -- --timeout 5m`. For multi-targeted projects such as `Kevlar.Tests`, add `-f net10.0` or `-f net11.0` before `--no-build`.
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

- Open every agent-created PR as a draft (`gh pr create --draft`) and follow [issue-pr-loop § Draft review lifecycle](.agents/skills/issue-pr-loop/SKILL.md#draft-review-lifecycle), even outside the loop. Mark it ready only after every active reviewer approves the current head and findings are dispositioned, then arm auto-merge with `scripts/Enable-AutoMerge.ps1`; never call `gh pr merge --auto` directly. Pull-request CI runs once the PR is ready; `CI Passed` gates merging.
- Every review cycle, check both review threads (`pulls/<N>/reviews`, `pulls/<N>/comments`, GraphQL `reviewThreads`) and PR conversation comments (`issues/<N>/comments`). Reviewers differ: Claude often posts its review as a conversation comment, while other agents use review threads. A finding on either surface needs a disposition before merge.
- Resolve each PR review thread, whether a human or a bot opened it, as soon as you have dispositioned it: the fix is pushed to the PR head and your reply names the commit, or your reply pushes back on the finding with evidence. Leave a thread open only while it has no disposition. If the reviewer replies after your disposition, unresolve the thread and handle the reply.

## Autonomous self-improvement

Treat every problem you notice as work to act on, whether you find it while testing, developing, reading code, reviewing, or doing anything else. Examples include bugs, flaky or failing tests, incorrect docs, misleading comments, broken tooling, and gaps in these instructions.

- If the fix is trivial and low-risk, fix it as part of the current work and mention it in the PR description.
- If the fix is out of scope for the current work, raise a GitHub issue at the point you discover it so it can be fixed later. Include what you saw, where you saw it (file and line, command, or CI run), how to reproduce it, and the expected behavior. Search existing issues first, and add to a matching issue instead of opening a duplicate. Write it as [Writing issues](#writing-issues) describes.
- Do not ignore a problem because it existed before your change or is unrelated to it.

### Writing issues

Every issue must be understood by a reader who has never seen Kevlar, from its title and first paragraph alone. This matters most in another project's repository: its maintainers do not know our code, terms or constraints, and an issue they cannot follow gets ignored or closed.

- **Title:** the observed problem or the requested capability in that project's own API terms, for example "`BasicPublishAsync` rejects `ReadOnlyMemory<byte>` header values". No Kevlar names or shorthand.
- **Summary first:** open with two or three plain sentences: what happens, what you expected, and why a user of that project would care. Details come after.
- **Their vocabulary, not ours:** do not use Kevlar's internal type, component or decision names, or our own targets and budgets; say what the code does instead. Use the project's issue template when it has one.
- **Short sections:** Summary, Reproduction, Expected and actual, Environment (exact versions); for a feature, Proposal instead of Expected and actual. The reproduction is minimal and self-contained, never needs Kevlar, and source links are pinned to the tag you checked.
- **One ask, stated once:** one problem per issue. End with a direct question or request, not a menu of options.
- **Cut what the maintainer does not need to act:** investigation narrative, hedging, our workarounds, local paths, worktree names, internal CI details. Mention Kevlar once, at the end, with a link to the tracking issue.
- **Check before filing:** search the project's open and closed issues, and its `main` branch and unreleased release notes, not only the version we use.
- **Reread as the maintainer:** could they tell within 30 seconds what is wrong and what you are asking? If a maintainer says an issue is unclear, rewrite the body in place, then reply with a short plain summary.
