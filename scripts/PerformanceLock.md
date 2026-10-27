# Shared local performance lock

All agents working in Kevlar, Respire, Reservoir, or Dekaf on this machine reserve the same Redis `performance` lock before local benchmarks, profiling, stress runs, builds, tests, restores, docs builds, or other sustained CPU-, memory-, or disk-intensive work.

Use **`scripts/AgentLocks.ps1` from the current canonical shared Dekaf checkout** for performance reservations. Set `SHARED_PERFORMANCE_LOCK_CHECKOUT` to its absolute path when it is installed outside the default `C:/git/Dekaf`. For this machine's `D:/git` layout, set this in the agent's environment before starting work:

```powershell
$env:SHARED_PERFORMANCE_LOCK_CHECKOUT = 'D:/git/Dekaf'
```

Configure the same value for every cooperating repository and agent on the machine, including agents running in linked worktrees. Do not infer it from the working directory or search sibling checkouts. An explicit override replaces the default; an invalid override must fail rather than trying the default. This changes only the checkout location: keep container `dekaf-agent-locks-redis`, key `dekaf:agent-lock:performance`, and Dekaf's owner-scoped token cache. Do not edit backend names or copy the script into Kevlar. Calling this repository's own `AgentLocks.ps1 -LockName performance` selects a different backend and does not coordinate with the other repositories.

Keep `$agentLocks` pointing at this repository's shared-checkout script for PR/issue ownership; use a separate `$performanceLocks` variable. Acquire the item lock first and release it last. Never pass `-Worktree` for `performance`, and always supply its explicit lock name on every verb.

Read the configured shared Dekaf checkout's `scripts/PerformanceLock.md` before heavy work, including from older branches. The [maintained upstream guidance](https://github.com/thomhurst/Dekaf/blob/main/scripts/PerformanceLock.md) may change its benchmark requirements; Kevlar still requires the local reservation described here. If either configured file or Redis is unavailable, defer heavy work and report the configured path or backend error. Correct the shared configuration or restore the canonical backend before retrying; do not silently fall back to another checkout or a repository-local lock. Reading and editing may continue.

## Reservation

Use the unit's `KEVLAR_AGENT_LOCK_OWNER_ID` when set, otherwise Codex's `CODEX_THREAD_ID`. Other automation must supply a stable unique owner identity in `KEVLAR_AGENT_LOCK_OWNER_ID` before running this example. Pass the captured value explicitly as `-OwnerId` on every call: Dekaf's script does not read Kevlar's owner variable and may have a different owner override of its own. Keep the checkout path and owner fixed until release, even if the environment changes. Capture acquisition output without printing the token.

```powershell
$performanceCheckout = if ([string]::IsNullOrWhiteSpace($env:SHARED_PERFORMANCE_LOCK_CHECKOUT)) {
    'C:/git/Dekaf'
} else {
    $env:SHARED_PERFORMANCE_LOCK_CHECKOUT
}
if (-not [IO.Path]::IsPathFullyQualified($performanceCheckout)) {
    throw 'SHARED_PERFORMANCE_LOCK_CHECKOUT must name an absolute shared Dekaf checkout.'
}
$performanceLocks = Join-Path $performanceCheckout 'scripts/AgentLocks.ps1'
$performanceWorkflow = Join-Path $performanceCheckout 'scripts/PerformanceLock.md'
foreach ($requiredFile in @($performanceLocks, $performanceWorkflow)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Canonical performance-lock file missing: $requiredFile. Defer heavy work; fix the shared configuration."
    }
}
# Read $performanceWorkflow before acquiring the reservation.
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

The lock coordinates cooperating agents; it cannot pause existing jobs or unrelated applications. Before timing, ensure competing heavy jobs and owned background services have stopped; do not stop another agent's processes or shared Redis. Record the reservation interval and observed interference with the evidence. Contaminated measurements are diagnostic, not proof of regression or acceptance.

Repository-specific command wrappers, resource limits, benchmark restrictions, and performance acceptance rules still apply. The local lock does not isolate a remote CI runner. Agents already running must read the updated instructions before their next heavy job.
