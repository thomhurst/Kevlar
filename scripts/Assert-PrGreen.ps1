# Assert-PrGreen.ps1
# Fail-closed merge gate for the issue-pr-loop skill.
#
# Exits 0 ONLY when PR #<N> is provably safe to merge:
#   - state OPEN and not a draft
#   - mergeable == MERGEABLE
#   - mergeStateStatus == CLEAN
#   - every status check is terminal AND passing (CheckRun: status=COMPLETED and
#     conclusion in SUCCESS/SKIPPED/NEUTRAL; StatusContext: state=SUCCESS), judged
#     on each workflow's newest run for the head
#   - every pull_request workflow on the head has run since the PR was last marked
#     ready (drafts skip CI, so a draft run's skipped jobs are not results)
#   - no current-head top-level review has actionable finding headings
#   - no unresolved review threads
#
# -ForAutoMerge is the gate for arming native auto-merge (Enable-AutoMerge.ps1) right
# after the PR is marked ready. CI may still be pending or not yet started, because
# GitHub then waits for the required `CI Passed` check itself, and a draft run cannot
# post that name. It therefore drops the CLEAN, terminal-check, and ran-since-ready
# rules, but still requires: OPEN, not draft, MERGEABLE, no failed check on any
# workflow's newest run, no actionable review body, and no unresolved threads.
#
# Any other state — pending/queued/in-progress checks, an empty rollup, a failed
# check, an unresolved thread, or an API hiccup — exits 1 so the caller MUST NOT
# merge. The default on uncertainty is DENY.
#
# Why this exists: branch protection can be absent, incomplete, or change over time.
# This script is the deterministic gate the prose rules alone cannot guarantee —
# the skill calls it immediately before `gh pr merge`, and only merges if it exits 0.
#
# Usage: pwsh scripts/Assert-PrGreen.ps1 -Pr 1234 [-Repo owner/name] [-ForAutoMerge]
# Exit:  0 = safe to merge (or arm); 1 = DO NOT MERGE (reason printed to stderr).

[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$Pr,
    [string]$Repo,
    [switch]$ForAutoMerge
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AssertPrGreenReviewHeuristics.ps1')

function Deny([string]$reason) {
    [Console]::Error.WriteLine("DO NOT MERGE #${Pr} -- $reason")
    exit 1
}

$repoArgs = @()
if ($Repo) { $repoArgs = @('--repo', $Repo) }

# Re-fetch fresh — survey output goes stale within seconds.
$raw = gh pr view $Pr @repoArgs --json number,state,isDraft,headRefOid,mergeable,mergeStateStatus,statusCheckRollup,latestReviews,commits 2>$null
if ($LASTEXITCODE -ne 0) { Deny "gh pr view failed (exit $LASTEXITCODE)" }
try {
    $view = $raw | ConvertFrom-Json
}
catch {
    Deny "could not parse PR state: $($_.Exception.Message)"
}

if ($view.state -ne 'OPEN') { Deny "state=$($view.state) (need OPEN)" }
if ($view.isDraft) { Deny "PR is a draft; mark it ready after review approval" }
if ($view.mergeable -ne 'MERGEABLE') { Deny "mergeable=$($view.mergeable) (need MERGEABLE)" }
if (-not $ForAutoMerge -and $view.mergeStateStatus -ne 'CLEAN') { Deny "mergeStateStatus=$($view.mergeStateStatus) (need CLEAN)" }

$checks = @(Select-LatestRunStatusChecks $view.statusCheckRollup)
if ($checks.Count -eq 0) {
    Deny "statusCheckRollup is empty -- no checks registered yet; refusing to merge blind"
}

$allowedConclusions = @('SUCCESS', 'SKIPPED', 'NEUTRAL')
# Auto-merge waits for pending CI itself. Unknown states fail closed rather than
# being mistaken for pending work.
$pendingStatuses = @('QUEUED', 'IN_PROGRESS', 'WAITING', 'PENDING', 'REQUESTED')
$pendingStates = @('PENDING', 'EXPECTED')
$bad = @()
foreach ($c in $checks) {
    if ($null -ne $c.status) {
        # CheckRun: must be terminal AND passing.
        if ($c.status -ne 'COMPLETED') {
            if ($ForAutoMerge -and $pendingStatuses -contains $c.status) { continue }
            $bad += "$($c.name): status=$($c.status)"
            continue
        }
        if ($allowedConclusions -notcontains $c.conclusion) {
            $bad += "$($c.name): conclusion=$($c.conclusion)"
        }
    }
    elseif ($null -ne $c.state) {
        # StatusContext: state must be SUCCESS.
        if ($c.state -ne 'SUCCESS') {
            if ($ForAutoMerge -and $pendingStates -contains $c.state) { continue }
            $bad += "$($c.context): state=$($c.state)"
        }
    }
    else {
        $bad += "unrecognized check entry: $($c | ConvertTo-Json -Compress)"
    }
}
if ($bad.Count -gt 0) {
    Deny ("checks not green: " + ($bad -join '; '))
}

if ($Repo) {
    $owner, $name = $Repo -split '/', 2
}
else {
    $nwo = gh repo view --json nameWithOwner --jq '.nameWithOwner' 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $nwo) { Deny "could not resolve repo" }
    $owner, $name = $nwo.Trim() -split '/', 2
}

# Immediate merge only: auto-merge waits for the required `CI Passed` check instead.
if (-not $ForAutoMerge) {
    $readyEvents = @(gh api --paginate "repos/$owner/$name/issues/$Pr/timeline?per_page=100" --jq '.[] | select(.event == "ready_for_review") | .created_at' 2>$null)
    if ($LASTEXITCODE -ne 0) { Deny "could not fetch PR timeline (exit $LASTEXITCODE)" }
    $lastReadyAt = @($readyEvents | Where-Object { $_ } | ForEach-Object { ConvertTo-UtcDateTimeOffset $_ } | Sort-Object) | Select-Object -Last 1
    if ($null -ne $lastReadyAt) {
        $runs = @(gh api --paginate "repos/$owner/$name/actions/runs?head_sha=$($view.headRefOid)&event=pull_request&per_page=100" --jq '.workflow_runs[] | {name, created_at}' 2>$null | ForEach-Object { $_ | ConvertFrom-Json })
        if ($LASTEXITCODE -ne 0) { Deny "could not list workflow runs for the head (exit $LASTEXITCODE)" }
        $notRun = @(Get-WorkflowsNotRunSinceReady -Runs $runs -ReadyAtUtc $lastReadyAt)
        if ($notRun.Count -gt 0) {
            Deny ("workflows have not run since the PR was marked ready: " + ($notRun -join '; '))
        }
    }
}

$headCommit = @($view.commits | Where-Object { $null -ne $_ }) | Select-Object -Last 1
$headCommittedAt = if ($headCommit) { ConvertTo-UtcDateTimeOffset $headCommit.committedDate } else { $null }

# Top-level review comments are not review threads, so GitHub does not expose a
# resolved flag for them. Fail closed on common review-finding shapes instead
# of treating a COMMENTED review as automatically safe. A stale Claude review
# from before the current head commit is ignored only when the current-head
# claude-review check completed green.
$latestReviews = @($view.latestReviews | Where-Object { $null -ne $_ })
$requestedChanges = @($latestReviews | Where-Object { $_.state -eq 'CHANGES_REQUESTED' })
if ($requestedChanges.Count -gt 0) {
    $authors = ($requestedChanges | ForEach-Object { $_.author.login }) -join ', '
    Deny "latest review requests changes from: $authors"
}

$actionableReviews = @()
foreach ($review in $latestReviews) {
    if ($review.state -ne 'COMMENTED') { continue }
    if (Test-StaleBotReviewCanBeIgnored -Review $review -HeadCommitCommittedAt $headCommittedAt -Checks $checks) {
        continue
    }

    $body = [string]$review.body
    $reason = Get-ActionableReviewBodyReason -Body $body
    if ($reason) {
        $actionableReviews += "$($review.author.login): $reason"
    }
}
if ($actionableReviews.Count -gt 0) {
    Deny ("latest top-level review comment has actionable finding(s): " + ($actionableReviews -join '; '))
}

# Unresolved review threads block merge even when a review's state is COMMENTED.
$query = 'query($owner:String!,$repo:String!,$pr:Int!,$after:String){repository(owner:$owner,name:$repo){pullRequest(number:$pr){reviewThreads(first:100,after:$after){pageInfo{hasNextPage endCursor} nodes{isResolved}}}}}'
$after = $null
$unresolved = 0
do {
    $cursorArgs = if ($after) { @('-f', "after=$after") } else { @() }
    $threadsRaw = gh api graphql -f query=$query -F owner=$owner -F repo=$name -F pr=$Pr @cursorArgs 2>$null
    if ($LASTEXITCODE -ne 0) { Deny "could not fetch review threads (exit $LASTEXITCODE)" }
    try {
        $reviewThreads = ($threadsRaw | ConvertFrom-Json).data.repository.pullRequest.reviewThreads
    }
    catch {
        Deny "could not parse review threads: $($_.Exception.Message)"
    }
    if ($null -eq $reviewThreads) { Deny "review-thread response was empty" }

    $unresolved += @($reviewThreads.nodes | Where-Object { -not $_.isResolved }).Count
    $after = $reviewThreads.pageInfo.endCursor
    if ($reviewThreads.pageInfo.hasNextPage -and -not $after) {
        Deny "review-thread response omitted the next-page cursor"
    }
} while ($reviewThreads.pageInfo.hasNextPage)

if ($unresolved -gt 0) { Deny "$unresolved unresolved review thread(s)" }

if ($ForAutoMerge) {
    Write-Host "OK #${Pr} -- ready, MERGEABLE, no failed checks, no unresolved threads. Safe to arm auto-merge."
}
else {
    Write-Host "OK #${Pr} -- MERGEABLE, CLEAN, $($checks.Count) check(s) green, no unresolved threads. Safe to merge."
}
exit 0
