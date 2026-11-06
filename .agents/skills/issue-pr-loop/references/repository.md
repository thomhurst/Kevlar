# Kevlar

Before heavy local work, follow [the local performance lock workflow](../../../../scripts/PerformanceLock.md). Reserve `performance` through this repository's `scripts/AgentLocks.ps1`, the same script and backend as `$agentLocks`, with the same explicit owner ID for every performance-lock verb. Acquire it after the item lock and release it first. If the backend is unavailable, defer heavy work.

Read [AGENTS.md](../../../../AGENTS.md) for commands, test matrix, commit style, and docs conventions.

- Preserve supported target frameworks from the project files. Forward cancellation and preserve analyzer `KEV001` semantics when changing execution overloads.
- Shields are immutable/thread-safe; reused instances share strategy state. Strategy, pooling, and composition changes need concurrency coverage.
- Shield execution, pipelines, `Outcome<T>`, pooled contexts, delay helpers, state-passing overloads, and HTTP handlers require relevant before/after benchmarks with throughput and allocations in the PR. Material regressions block merging; preserve synchronous `ValueTask` paths and avoid added closures, boxing, contention, or scans during review/simplification.
- No Aspire AppHost. Integration tests use in-process fakes and loopback HTTP; Docker serves only shared lock Redis.

The mechanics the loop defers to:

| Need | Owner |
|---|---|
| One PR's review cycle: fetch every surface, verify findings, fix, one push, reply, resolve and unresolve threads | [Review cycle](#review-cycle) below, with `AGENTS.md` § Pull request reviews |
| PR review lifecycle | [draft-review.md](draft-review.md), with [Draft review lifecycle](#draft-review-lifecycle) below |
| Merge protocol | [Merge gate](#merge-gate) below |
| Out-of-scope filing bar and procedure | `AGENTS.md` § Autonomous self-improvement and § Writing issues |
| Native sub-issue and `blocked_by` writes | [Issue relationships](#issue-relationships) below |
| Worktree location, evidence directory, cleanup | [Worktrees](#worktrees) below and [WorktreeLifecycle.md](../../../../scripts/WorktreeLifecycle.md) |

Lock owner variable: `KEVLAR_AGENT_LOCK_OWNER_ID` (read by `scripts/AgentLocks.ps1` after `-OwnerId`, before `CODEX_THREAD_ID`).

Use commentary while active.

## Issue priority

This repository has no `priority:*` or `ci` labels, so every issue sits in one tier in [pick-an-issue.md](pick-an-issue.md) and the within-tier order decides. `scripts/Start-IssuePrLoop.ps1` reads no priority label: it ranks finished parents first, then issues that block the most others.

## Owner-reserved decisions

No issue class is reserved for the owner beyond the `[human]` list in `SKILL.md`, so `needs-human` follows the normal triage rules. A breaking change is allowed on a milestone that permits breaking changes. Enabling the `Require CI checks on main` ruleset is the owner's call.

## Draft review lifecycle

Lifecycle: [draft-review.md](draft-review.md), with these repository rules on top.

- "Both reviewers" in draft-review.md means every active reviewer below.
- Marking ready does not re-run Claude review: `ready_for_review` is not one of its triggers. Readiness starts CI on the reviewed head without changing it, so the draft review satisfies the review cycle.
- When `Enable-AutoMerge.ps1` refuses because `main` does not require `CI Passed`, auto-merge would not wait for CI: leave it unarmed and merge through [Merge gate](#merge-gate) once CI is green.
- When CI fails on a ready PR, restore draft with `gh pr ready <N> --undo` (this cancels any in-progress run), fix it as a CI failure, and return to step 1 for the new head. Codex is only tagged on drafts, so this is how a fix head gets its review.

Draft policy: `AGENTS.md` applies the skill's draft review lifecycle to every agent-created PR, including work outside the loop, which replaces the loop-only scope in draft-review.md. Before creating or promoting a PR from an existing worktree, re-read `AGENTS.md` and this skill from the shared checkout; branch copies can predate the current shipping policy.

Active reviewers:

- **Codex** (`chatgpt-codex-connector[bot]`) reviews when requested with `@codex review`, as [draft-review.md](draft-review.md) describes. Its clean receipt reads `Codex Review: Didn't find any major issues`.
- **Claude Code Review** (`.github/workflows/claude-code-review.yml`) reviews drafts and every new head (`opened`, `synchronize`, `reopened`). It runs from `main` under `pull_request_target`, so a PR that edits the workflow is still reviewed by `main`'s version. It posts a review only while the `CLAUDE_CODE_OAUTH_TOKEN` secret is configured. Otherwise the `claude-review` check succeeds after the `Report unconfigured optional review` step and posts nothing: Claude is then not an active reviewer, and its green check is not a verdict. Require a Claude verdict only when the current head's run executed `Run Claude Code Review`.
- CodeRabbit comments on PRs from a rate-limited free tier. Treat its findings as review feedback; its approval is not required.

Draft CI: `ci.yml`, `outage.yml`, and `contention-profile.yml` skip every job of a draft `pull_request` run before a runner is allocated. `ready_for_review` starts them; `converted_to_draft` cancels an in-progress run through workflow concurrency. Push, schedule, and manual dispatch runs are unaffected. `ci.yml` also runs the model sweep (`model-tests.yml`) and the 30-second stress smoke (`stress-run.yml`) as called workflows when their paths change, so they are part of CI rather than separate pull-request workflows.

Required check: the `Require CI checks on main` ruleset requires only `CI Passed` from GitHub Actions (app 15368), the `ci-passed` aggregator in `ci.yml`. The ruleset is currently **disabled**: enforcing it would reject the measurement-docs pushes that `stress.yml`, `benchmarks.yml`, and `outage.yml` make to `main` with `GITHUB_TOKEN`, and a personal-account repository cannot add GitHub Actions as a bypass actor. Until the owner enables it, `Enable-AutoMerge.ps1` refuses to arm, so merge with `Merge-Pr.ps1` once CI is green. It needs every CI job, passes path-skipped legs, and fails on a failed or cancelled job. A draft run skips it, and GitHub reports a skipped job under its unevaluated name expression, so a draft cannot satisfy the requirement. Add every new CI job to its `needs`. `outage.yml` and `contention-profile.yml` are advisory measurement and diagnostics runs: they do not block merging, but a failure on the PR still denies `Assert-PrGreen.ps1`.

Merging: after `gh pr ready`, arm auto-merge with `Enable-AutoMerge.ps1`. It refuses unless `main` actively requires `CI Passed`, runs `Assert-PrGreen.ps1 -ForAutoMerge` (pending CI allowed; failed checks, drafts, actionable review bodies, and unresolved threads denied), and arms squash auto-merge pinned to the reviewed head. The `disarm-auto-merge` job in `claude-code-review.yml` disarms it on every push. `Merge-Pr.ps1` remains for a PR whose checks are already terminal: without `-ForAutoMerge`, `Assert-PrGreen.ps1` judges each workflow's newest run for the head and denies until every `pull_request` workflow has run since the PR was last marked ready.

Automated review rounds (rework budget): Claude posts as `github-actions[bot]` conversation comments, with no marker, and Codex leaves one `codex-review-receipt:v1` per round on either surface. Add the two counts:

```powershell
gh api --paginate "repos/{owner}/{repo}/issues/<N>/comments" --jq '.[] | select(.user.login == "github-actions[bot]" or (.user.login == "chatgpt-codex-connector[bot]" and (.body | contains("codex-review-receipt:v1")))) | .id' | wc -l
gh api --paginate "repos/{owner}/{repo}/pulls/<N>/reviews" --jq '.[] | select(.user.login == "chatgpt-codex-connector[bot]" and (.body | contains("codex-review-receipt:v1"))) | .id' | wc -l
```

## Merge gate

The `Required check` and `Merging` rules above, plus:

- Merge requires an open, ready-for-review PR; `MERGEABLE` and `CLEAN`; every applicable check present, terminal, and passing (`SUCCESS`, `SKIPPED`, or `NEUTRAL`, with successful legacy commit statuses); no unresolved threads, unaddressed comments, or outstanding review-body concerns; approval when required; and a bot review or CI cycle after the last fix push. Missing expected checks and nonterminal checks block merging. Reassess if the head changes.
- After a fix push, allow that cycle before considering merge in a later iteration.
- `Merge-Pr.ps1` calls `Assert-PrGreen.ps1`, merges on a passing mechanical gate, and cleans up. A ready PR from the lifecycle merges through auto-merge; use the wrapper when a PR's checks are already terminal and green.
- Read every review body, inline thread, and conversation comment on the current head first. The scripts are necessary but not sufficient.
- Never call `gh pr merge` or `--auto` directly; arm auto-merge only with `Enable-AutoMerge.ps1`.

## Review cycle

Run this under the PR lock whenever a PR has feedback. `AGENTS.md` § Pull request reviews owns the surfaces and thread-resolution policy; this section is the full procedure.

**Fetch.** Inspect all review surfaces, including pagination and replies: REST `pulls/<N>/reviews`, `pulls/<N>/comments`, and `issues/<N>/comments` under `repos/{owner}/{repo}`, and GraphQL `reviewThreads` with IDs, resolution state, bodies, authors, and timestamps.

Never infer a reviewer's surface from its identity: check both every cycle. Treat a conversation comment that carries findings or a verdict as a review; it has no thread to resolve, so disposition it with one PR comment that quotes or links it.

A `COMMENTED` review body can block with zero unresolved threads. Address outstanding concerns, `CHANGES_REQUESTED`, current inline findings, and unresolved threads. Empty acknowledgments are non-blocking. Assess older findings against the current head and later discussion; uncertain concerns block merging.

**Classify.** Verify every finding against the code.

- **Blocking:** a correctness, concurrency, security, contract, or performance defect, or missing tests for changed behavior. Fix each valid blocking finding in this PR.
- **Non-blocking:** naming, style, wording, optional refactors, extra tests for unchanged behavior, and "consider" suggestions. Implement one only when it is small and you already push a blocking fix. Otherwise decline it with a one-line reason and push nothing for it.
- Decline a wrong finding with evidence from the code. Move genuinely out-of-scope architectural work to a filed follow-up issue, linked in the reply.
- "Not done" or "left as is" without a classification, reason, or linked issue is not a disposition.

**Reply.** Reply once per review covering each finding, and reply on each thread. Discuss disagreement once; implement reaffirmed blocking feedback. A disposition-only reply on the current head needs no new push, so it starts no review cycle. Reply to addressed review-body findings and request re-review when an outstanding changes-requested review needs updating; never dismiss reviews to bypass approval requirements.

**Resolve.** Resolve every thread, human or bot, with GraphQL `resolveReviewThread` as soon as it is dispositioned: the fix commit is in the remote PR head and the reply names it, or the reply pushes back with evidence. Do not wait for another review or CI cycle; that cycle still gates merging. Leave a thread open only while it has no disposition: the fix is not pushed, the item waits on the user, or the reviewer replied after your disposition. On each later cycle re-fetch every thread, resolved ones included. A reviewer reply newer than your disposition reopens it: run GraphQL `unresolveReviewThread` and treat it as reaffirmed feedback. Then re-fetch reviews, threads, checks, and head SHA.

## Issue relationships

Writes take the numeric REST issue `.id`, not the issue number or `gh issue view --json id` (a GraphQL ID). Pass it with `-F` so it is sent as an integer:

```powershell
$childId = gh api "repos/$repoSlug/issues/<child>" --jq .id
gh api "repos/$repoSlug/issues/<parent>/sub_issues" -X POST -F "sub_issue_id=$childId"

$blockerId = gh api "repos/$repoSlug/issues/<blocker>" --jq .id
gh api "repos/$repoSlug/issues/<blocked>/dependencies/blocked_by" -X POST -F "issue_id=$blockerId"
```

Verify each write with a GET on the same path before proceeding.

## Worktrees

Capture these with the skill's shared-checkout variables:

```powershell
$repoName = ($repoSlug -split '/')[-1]
$worktreeRoot = Join-Path (Split-Path $repo -Parent) ($repoName + '-worktrees')
```

- Create worktrees under `$worktreeRoot`; use a writable temporary `<repoName>-worktrees` root if needed. Verify the repository and intended base; examples use `origin/main`.
- This repository does not use sparse-checkout.
- First push of a branch: `git push -u origin HEAD`. A branch created from `origin/main` can track it, so never rely on a bare `git push`.
- Use repository scripts for merged-worktree cleanup; `[gone]` or ancestry alone is insufficient. Preserve dirty, locked, open-PR, harness-managed, and locally divergent worktrees. Inspect untracked source before cleanup: scripts may clear build artifacts. Manually remove an abandoned worktree only after verifying repository, path, ownership, and absence of valuable unpublished work.
- Stop only services, processes, and containers started for this worktree. Never blanket-delete containers or volumes, or stop the shared lock Redis.

### Agent output locations

Write nothing loose beside the repository. Every file an agent produces outside its worktree has one home, so cleanup can find it:

- Evidence (build and test logs, reports, PR and comment body drafts, diffs, validation projects) goes in `<repo-parent>/<repoName>-evidence/<lock-name>/`, for example `C:/git/Kevlar-evidence/issue-123/build.log`. `scripts/Remove-MergedWorktrees.ps1` deletes an entry once its newest file is older than 7 days (`-EvidenceRetentionDays`).
- Throwaway checkouts and baselines are worktrees under `$worktreeRoot`, never plain copies.
- Downloaded SDKs and CLI tools go in `C:/git/toolchains/<name>-<version>/`, shared by every repository.
- Never write to the repository's parent directory (such as `C:/git`) itself or to the main checkout.

## Implement and ship

- Follow local validation, resource limits, target frameworks, generated-code boundaries, and performance requirements. Use TDD for bugs and behavior changes where feasible; explain exceptions. Run focused and relevant broader checks, including docs and website checks when affected.
- Review the full diff for correctness, reuse, and efficiency (`/simplify` when available, otherwise equivalent review). Preserve performance and contracts, and batch related fixes into one push.
- Follow [the PR template](../../../../.github/PULL_REQUEST_TEMPLATE.md).
