# Kevlar

Before heavy local work, follow [the shared performance lock workflow](../../../../scripts/PerformanceLock.md). Resolve the canonical shared Dekaf checkout from `SHARED_PERFORMANCE_LOCK_CHECKOUT` (default `C:/git/Dekaf`), using the same setting in every cooperating repository. Reserve `performance` through that checkout's `scripts/AgentLocks.ps1`, using `$performanceLocks` separately from this repository's `$agentLocks` and the same explicit owner ID for every performance-lock verb. Acquire it after the item lock and release it first. If the configured checkout or backend is unavailable, defer heavy work; repository-local Redis namespaces do not provide cross-repository isolation.

Read [AGENTS.md](../../../../AGENTS.md) for commands, test matrix, commit style, and docs conventions.

- Preserve supported target frameworks from the project files. Forward cancellation and preserve analyzer `KEV001` semantics when changing execution overloads.
- Shields are immutable/thread-safe; reused instances share strategy state. Strategy, pooling, and composition changes need concurrency coverage.
- Shield execution, pipelines, `Outcome<T>`, pooled contexts, delay helpers, state-passing overloads, and HTTP handlers require relevant before/after benchmarks with throughput and allocations in the PR. Material regressions block merging; preserve synchronous `ValueTask` paths and avoid added closures, boxing, contention, or scans during review/simplification.
- No Aspire AppHost. Integration tests use in-process fakes and loopback HTTP; Docker serves only shared lock Redis.

## Draft review lifecycle

Draft policy: `AGENTS.md` applies the skill's draft review lifecycle to every agent-created PR, including work outside the loop. Before creating or promoting a PR from an existing worktree, re-read `AGENTS.md` and this skill from the shared checkout; branch copies can predate the current shipping policy.

Active reviewers:

- **Codex** (`chatgpt-codex-connector[bot]`) reviews when requested with `@codex review`, as the skill describes. Its clean receipt reads `Codex Review: Didn't find any major issues`.
- **Claude Code Review** (`.github/workflows/claude-code-review.yml`) reviews drafts and every new head (`opened`, `synchronize`, `reopened`). It runs from `main` under `pull_request_target`, so a PR that edits the workflow is still reviewed by `main`'s version. It posts a review only while the `CLAUDE_CODE_OAUTH_TOKEN` secret is configured. Otherwise the `claude-review` check succeeds after the `Report unconfigured optional review` step and posts nothing: Claude is then not an active reviewer, and its green check is not a verdict. Require a Claude verdict only when the current head's run executed `Run Claude Code Review`.
- CodeRabbit comments on PRs from a rate-limited free tier. Treat its findings as review feedback; its approval is not required.

Draft CI: `ci.yml`, `outage.yml`, and `contention-profile.yml` skip every job of a draft `pull_request` run before a runner is allocated. `ready_for_review` starts them; `converted_to_draft` cancels an in-progress run through workflow concurrency. Push, schedule, and manual dispatch runs are unaffected. `ci.yml` also runs the model sweep (`model-tests.yml`) and the 30-second stress smoke (`stress-run.yml`) as called workflows when their paths change, so they are part of CI rather than separate pull-request workflows.

Required check: the `Require CI checks on main` ruleset requires only `CI Passed` from GitHub Actions, the `ci-passed` aggregator in `ci.yml`. It needs every CI job, passes path-skipped legs, and fails on a failed or cancelled job. A draft run skips it, and GitHub reports a skipped job under its unevaluated name expression, so a draft cannot satisfy the requirement. Add every new CI job to its `needs`. `outage.yml` and `contention-profile.yml` are advisory measurement and diagnostics runs: they do not block merging, but a failure on the PR still denies `Assert-PrGreen.ps1`.

Merging: after `gh pr ready`, arm auto-merge with `Enable-AutoMerge.ps1`. It refuses unless `main` actively requires `CI Passed`, runs `Assert-PrGreen.ps1 -ForAutoMerge` (pending CI allowed; failed checks, drafts, actionable review bodies, and unresolved threads denied), and arms squash auto-merge pinned to the reviewed head. The `disarm-auto-merge` job in `claude-code-review.yml` disarms it on every push. `Merge-Pr.ps1` remains for a PR whose checks are already terminal: without `-ForAutoMerge`, `Assert-PrGreen.ps1` judges each workflow's newest run for the head and denies until every `pull_request` workflow has run since the PR was last marked ready.
