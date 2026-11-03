# Local performance lock

All agents working in Kevlar on this machine, including those in linked worktrees, reserve the Redis `performance` lock before local benchmarks, profiling, stress runs, builds, tests, restores, docs builds, or other sustained CPU-, memory-, or disk-intensive work.

Reserve it with **this repository's `scripts/AgentLocks.ps1`**. Run the script from the main Kevlar checkout or from any worktree; every copy uses the same backend: container `kevlar-agent-locks-redis`, key `kevlar:agent-lock:performance`. Do not use another repository's lock script or set `SHARED_PERFORMANCE_LOCK_CHECKOUT`. Dekaf and Reservoir no longer use a shared cross-repository `performance` lock, so this reservation coordinates Kevlar agents only.

PR/issue ownership locks use the same script and backend under different lock names (`pr-<N>`, `issue-<N>`). Acquire the item lock first and release it last. Never pass `-Worktree` for `performance`, and always supply its explicit lock name on every verb.

If Redis or Docker is unavailable, defer heavy work and report the backend error. Reading and editing may continue.

## Reservation

Use the unit's `KEVLAR_AGENT_LOCK_OWNER_ID` when set, otherwise Codex's `CODEX_THREAD_ID`. Other automation, including Claude Code sessions, must set a stable unique owner identity in `KEVLAR_AGENT_LOCK_OWNER_ID` before running this example. Pass the captured value explicitly as `-OwnerId` on every call, and keep it fixed until release, even if the environment changes. Capture acquisition output without printing the token.

```powershell
$performanceLocks = Join-Path (git rev-parse --show-toplevel) 'scripts/AgentLocks.ps1'
$performanceOwnerId = if (-not [string]::IsNullOrWhiteSpace($env:KEVLAR_AGENT_LOCK_OWNER_ID)) {
    $env:KEVLAR_AGENT_LOCK_OWNER_ID
} else {
    $env:CODEX_THREAD_ID
}
if ([string]::IsNullOrWhiteSpace($performanceOwnerId)) {
    throw 'Set a stable KEVLAR_AGENT_LOCK_OWNER_ID before reserving performance.'
}
$lockOutput = & pwsh -NoProfile -File $performanceLocks acquire -LockName performance -OwnerId $performanceOwnerId
$lockExit = $LASTEXITCODE
if ($lockExit -eq 3) { return } # Defer heavy work; reading/editing can continue.
if ($lockExit -ne 0) { throw 'Performance lock unavailable; do not start heavy work.' }

try {
    # Run the bounded workload here using this repository's required command wrappers.
    # Check its exit status and stop/observe all owned heavy child processes before release.
    $lockStatus = & pwsh -NoProfile -File $performanceLocks status -LockName performance -OwnerId $performanceOwnerId
    if ($LASTEXITCODE -ne 0 -or $lockStatus -ne 'HELD-BY-ME') {
        throw 'Performance ownership lost; any measurements are inconclusive.'
    }
}
finally {
    & pwsh -NoProfile -File $performanceLocks release -LockName performance -OwnerId $performanceOwnerId
    if ($LASTEXITCODE -ne 0) { throw 'Performance lock release failed; inspect ownership without deleting the key.' }
}
```

Acquire once around the entire operation; child scripts must not reacquire or release it. Checking for a free key without acquiring it leaves a race. Hold one reservation across preparation and the complete baseline/candidate/control experiment, including any approved repeat. Do not run another heavy workload alongside your own measurements.

The lease lasts two hours. Bound each command to finish within the remaining lease, with cleanup time. Renew before a phase when needed using `pwsh -NoProfile -File $performanceLocks renew -LockName performance -OwnerId $performanceOwnerId`; check ownership before measurement phases and after completion. On expiry, failed verification, or renew exit 4, stop owned heavy work and mark affected measurements inconclusive. Never steal the key, busy-poll, or hold it while waiting for CI, reviews, or user input.

The lock coordinates cooperating Kevlar agents; it cannot pause existing jobs, other repositories' agents, or unrelated applications. Before timing, ensure competing heavy jobs and owned background services have stopped; do not stop another agent's processes or shared Redis. Record the reservation interval and observed interference with the evidence. Contaminated measurements are diagnostic, not proof of regression or acceptance.

Repository-specific command wrappers, resource limits, benchmark restrictions, and performance acceptance rules still apply. The local lock does not isolate a remote CI runner. Agents already running must read the updated instructions before their next heavy job.
