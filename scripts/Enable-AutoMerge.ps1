# Enable-AutoMerge.ps1
# Arms GitHub native auto-merge (squash) for a ready PR, pinned to its reviewed head.
#
#   1. captures the head, then checks that the default branch actively requires the
#      `CI Passed` check. GitHub auto-merge waits only for required checks, so without
#      that rule arming would merge at once, before CI.
#   2. runs  Assert-PrGreen.ps1 -ForAutoMerge  (ready, MERGEABLE, no failed check, no
#      actionable review body, no unresolved threads) and stops on any denial
#   3. arms  gh pr merge --auto --squash --match-head-commit <head>
#
# GitHub then merges once `CI Passed` succeeds on that head. A draft run skips the
# `CI Passed` job, which GitHub reports under its unevaluated name expression, so only
# a ready run can satisfy the requirement. A later push disarms auto-merge (the
# `disarm-auto-merge` job in claude-code-review.yml), so each new head needs its own
# review and a new call to this script.
#
# The caller must read and disposition every review body, inline finding, and issue
# comment, and confirm every active reviewer approved this head, BEFORE running this;
# the gate cannot judge review content.
#
# Auto-merge leaves the PR's worktree in place; scripts/Remove-MergedWorktrees.ps1
# removes it after the merge.
#
# Usage: pwsh scripts/Enable-AutoMerge.ps1 -Pr 1234 [-Repo owner/name]
# Exit:  0 = armed; 1 = not armed (reason printed to stderr).

[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$Pr,
    [string]$Repo
)

$ErrorActionPreference = 'Stop'
$requiredCheck = 'CI Passed'

function Fail([string]$reason) {
    [Console]::Error.WriteLine("NOT ARMED #${Pr} -- $reason")
    exit 1
}

$repoArgs = @()
if ($Repo) { $repoArgs = @('--repo', $Repo) }

# Capture the head before the gate so a push racing the gate cannot be armed unreviewed:
# --match-head-commit makes GitHub refuse if the head moved since.
$pr = gh pr view $Pr @repoArgs --json headRefOid,baseRefName 2>$null | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $pr.headRefOid) { Fail "could not read PR head (exit $LASTEXITCODE)" }
$head = $pr.headRefOid

$nwo = if ($Repo) { $Repo } else { (gh repo view --json nameWithOwner --jq '.nameWithOwner' 2>$null) }
if ($LASTEXITCODE -ne 0 -or -not $nwo) { Fail 'could not resolve repo' }
$required = @(gh api --paginate "repos/$($nwo.Trim())/rules/branches/$($pr.baseRefName)" --jq '.[] | select(.type == "required_status_checks") | .parameters.required_status_checks[].context' 2>$null)
if ($LASTEXITCODE -ne 0) { Fail "could not read the $($pr.baseRefName) branch rules (exit $LASTEXITCODE)" }
if ($required -notcontains $requiredCheck) {
    Fail "$($pr.baseRefName) does not actively require '$requiredCheck'; auto-merge would merge before CI"
}

$gateArgs = @('-Pr', $Pr, '-ForAutoMerge')
if ($Repo) { $gateArgs += @('-Repo', $Repo) }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Assert-PrGreen.ps1') @gateArgs
if ($LASTEXITCODE -ne 0) { Fail 'auto-merge gate denied (see above)' }

gh pr merge $Pr @repoArgs --auto --squash --match-head-commit $head
if ($LASTEXITCODE -ne 0) { Fail "gh pr merge --auto failed (exit $LASTEXITCODE)" }

Write-Host "Armed auto-merge for #${Pr} at $head."
exit 0
