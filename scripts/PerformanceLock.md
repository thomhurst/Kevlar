# Shared local performance lock

All agents working in Kevlar, Respire, Reservoir, or Dekaf on this machine reserve the same Redis `performance` lock before local benchmarks, profiling, stress runs, builds, tests, restores, docs builds, or other sustained CPU-, memory-, or disk-intensive work.

Use **`C:/git/Dekaf/scripts/AgentLocks.ps1` from the current shared Dekaf checkout** for performance reservations. This selects container `dekaf-agent-locks-redis` and key `dekaf:agent-lock:performance`, including Dekaf's owner-scoped token cache. Calling this repository's own `AgentLocks.ps1 -LockName performance` selects a different backend and does not coordinate with the other repositories.

Keep `$agentLocks` pointing at this repository's shared-checkout script for PR/issue ownership; use a separate `$performanceLocks` variable. Acquire the item lock first and release it last. Never pass `-Worktree` for `performance`, and always supply its explicit lock name on every verb.

Read the current shared Dekaf checkout's `scripts/PerformanceLock.md` before heavy work, including from older branches. The [maintained workflow](https://github.com/thomhurst/Dekaf/blob/main/scripts/PerformanceLock.md) defines reservation, lease, cleanup, and interference handling. If the shared checkout or Redis is unavailable, defer heavy work; do not silently fall back to a repository-local lock.

## Reservation

Codex supplies `CODEX_THREAD_ID`. Other automation must pass the same stable unique `-OwnerId` on every call; Dekaf's script does not read this repository's owner environment variable. Capture acquisition output without printing the token.

```powershell
$performanceLocks = 'C:/git/Dekaf/scripts/AgentLocks.ps1'
$lockOutput = & pwsh -NoProfile -File $performanceLocks acquire -LockName performance
$lockExit = $LASTEXITCODE
if ($lockExit -eq 3) { return } # Defer heavy work; reading/editing can continue.
if ($lockExit -ne 0) { throw 'Performance lock unavailable; do not start heavy work.' }

try {
    # Run the bounded workload here using this repository's required command wrappers.
    # Check its exit status and stop/observe all owned heavy child processes before release.
    $lockStatus = & pwsh -NoProfile -File $performanceLocks status -LockName performance
    if ($LASTEXITCODE -ne 0 -or $lockStatus -ne 'HELD-BY-ME') {
        throw 'Performance ownership lost; any measurements are inconclusive.'
    }
}
finally {
    & pwsh -NoProfile -File $performanceLocks release -LockName performance
    if ($LASTEXITCODE -ne 0) { throw 'Performance lock release failed; inspect ownership without deleting the key.' }
}
```

Acquire once around the entire operation; child scripts must not reacquire or release it. Checking for a free key without acquiring it leaves a race. Hold one reservation across preparation and the complete baseline/candidate/control experiment, including any approved repeat. Do not run another heavy workload alongside your own measurements.

The lease lasts two hours. Bound each command to finish within the remaining lease, with cleanup time. Renew before a phase when needed using `pwsh $performanceLocks renew -LockName performance`; check ownership before measurement phases and after completion. On expiry, failed verification, or renew exit 4, stop owned heavy work and mark affected measurements inconclusive. Never steal the key, busy-poll, or hold it while waiting for CI, reviews, or user input.

The lock coordinates cooperating agents; it cannot pause existing jobs or unrelated applications. Before timing, ensure competing heavy jobs and owned background services have stopped; do not stop another agent's processes or shared Redis. Record the reservation interval and observed interference with the evidence. Contaminated measurements are diagnostic, not proof of regression or acceptance.

Repository-specific command wrappers, resource limits, benchmark restrictions, and performance acceptance rules still apply. The local lock does not isolate a remote CI runner. Agents already running must read the updated instructions before their next heavy job.
