# WorktreeCleanup.ps1
# Shared worktree-removal helper, dot-sourced by Merge-Pr.ps1 and
# Remove-MergedWorktrees.ps1. Not meant to be run directly.
#
# Removal policy (one place, both callers):
#   - PRESERVE a worktree with uncommitted tracked changes or untracked files outside
#     known generated directories. Never force-discard possible work.
#   - CLEAR untracked build artifacts (node_modules/bin/obj/etc.) — they are not work.
#   - CLEAR root-level workflow output covered by the repository's .gitignore.
#   - DETACH directory junctions and symlinks before deleting, so no delete follows a link.
#   - Long-path safe: git's own delete now works because core.longpaths=true is set
#     system-wide; the `\\?\` extended-length Remove-Item is kept as a fallback for
#     environments where that config is missing.

function New-OrdinalStringMap {
    [CmdletBinding()]
    [OutputType([System.Collections.Generic.Dictionary[string, bool]])]
    param()

    return [System.Collections.Generic.Dictionary[string, bool]]::new([System.StringComparer]::Ordinal)
}

$script:DisposableWorktreeGeneratedDirectories = New-OrdinalStringMap
foreach ($directory in @(
    '.artifacts',
    '.vs',
    '__pycache__',
    'ARM',
    'ARM64',
    'artifacts',
    'benchmark-results',
    'BenchmarkDotNet.Artifacts',
    'bin',
    'bld',
    'CodeCoverage',
    'Debug',
    'DebugPublic',
    'log',
    'logs',
    'node_modules',
    'obj',
    'Release',
    'Releases',
    'results',
    'StrykerOutput',
    'temptest',
    'TestResults',
    'Win32',
    'x64',
    'x86'
)) {
    $script:DisposableWorktreeGeneratedDirectories[$directory] = $true
}

$script:DisposableWorktreeScopedDirectories = @(
    'docs/.cache',
    'docs/.docusaurus',
    'docs/build',
    'docs/static/api',
    'docs/playwright-report',
    'docs/test-results',
    'BenchmarkResults',
    # Root-level SDK installs and agent scratch space.
    '.dotnet',
    '.tmp'
)

function Test-DisposableWorktreePath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    # Quoted porcelain paths require Git's escape decoding. Preserve them rather than
    # risk classifying an unusual source path as generated output.
    if ($Path.StartsWith('"', [System.StringComparison]::Ordinal)) { return $false }

    $normalizedPath = $Path -replace '\\', '/'
    # Keep these root-only patterns aligned with .gitignore. Ignore rules alone
    # cannot authorize deleting arbitrary files: ignored source and secrets survive.
    if (-not $normalizedPath.Contains('/') -and
        ($normalizedPath -cmatch '\.(binlog|log|nettrace)$' -or
         $normalizedPath -cmatch '(^|-)(pr-body|review-disposition|review-validation|rebase-validation|comment|issue)\.md$')) {
        return $true
    }
    foreach ($docsGeneratedDirectory in $script:DisposableWorktreeScopedDirectories) {
        if ($normalizedPath -ceq $docsGeneratedDirectory -or
            $normalizedPath.StartsWith("$docsGeneratedDirectory/", [System.StringComparison]::Ordinal)) {
            return $true
        }
    }

    foreach ($segment in ($normalizedPath -split '/')) {
        if ($script:DisposableWorktreeGeneratedDirectories.ContainsKey($segment)) { return $true }
    }

    return $false
}

function Test-WorktreeMatchesMergedPullRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Associations,
        [AllowNull()][string]$Branch,
        [bool]$Detached
    )

    foreach ($association in $Associations) {
        if (-not $association.merged_at) { continue }
        if ($Detached) { return $true }
        if ($Branch -and $association.head -and $association.head.ref -ceq $Branch) { return $true }
    }

    return $false
}

function Test-WorktreeHeadMerged {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$Head,
        [Parameter(Mandatory)][string]$MergedHead
    )

    if ($Head -ceq $MergedHead) { return $true }
    git -C $Repo merge-base --is-ancestor $Head $MergedHead 2>$null
    return $LASTEXITCODE -eq 0
}

function Remove-MergedWorktree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Repo,       # a checkout that is NOT the one being removed (main)
        [Parameter(Mandatory)][string]$Worktree,   # path to remove
        [string]$Label = '',                       # e.g. "#1234" for log lines
        [string]$ExpectedHead,
        [string[]]$LockNames = @(),                # identities a pending retry must reserve
        [switch]$WhatIf
    )

    if (-not (Test-Path -LiteralPath $Worktree)) {
        if (-not $WhatIf) { git -C $Repo worktree prune }
        return
    }

    $repoPath = (Resolve-Path -LiteralPath $Repo).Path.TrimEnd('\', '/')
    $worktreePath = (Resolve-Path -LiteralPath $Worktree).Path.TrimEnd('\', '/')
    $relativePath = [IO.Path]::GetRelativePath($repoPath, $worktreePath)
    if ($relativePath -eq '.' -or (-not [IO.Path]::IsPathRooted($relativePath) -and
        $relativePath -ne '..' -and -not $relativePath.StartsWith("..$([IO.Path]::DirectorySeparatorChar)"))) {
        Write-Host "Preserving main or harness-managed worktree: $Worktree"
        return
    }
    $topLevel = git -C $Worktree rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($topLevel) -ne $worktreePath) {
        Write-Host "Preserving worktree $Label : $Worktree (could not verify repository path)"
        return
    }
    $gitDirectory = git -C $Worktree rev-parse --absolute-git-dir 2>$null
    if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath (Join-Path $gitDirectory 'locked'))) {
        Write-Host "Preserving locked or unreadable worktree: $Worktree"
        return
    }
    $commonDirectory = git -C $Worktree rev-parse --path-format=absolute --git-common-dir 2>$null
    if ($LASTEXITCODE -ne 0) { return }
    $repoCommonDirectory = git -C $Repo rev-parse --path-format=absolute --git-common-dir 2>$null
    if ($LASTEXITCODE -ne 0 -or $commonDirectory -ne $repoCommonDirectory -or $gitDirectory -eq $commonDirectory) {
        Write-Host "Preserving worktree from another repository or main checkout: $Worktree"
        return
    }
    $head = git -C $Worktree rev-parse HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or ($ExpectedHead -and $head -cne $ExpectedHead)) {
        Write-Host "Preserving worktree $Label : $Worktree (HEAD changed during sweep)"
        return
    }

    # Preserve tracked work and every untracked/ignored path that is not under a known
    # generated directory. --force below is safe only after this fail-closed check.
    $status = @(git -C $Worktree status --porcelain=v1 --untracked-files=all --ignored=matching 2>$null)
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Preserving worktree $Label : $Worktree (could not inspect worktree status)"
        return
    }

    $work = @($status | Where-Object {
        if ($_ -notmatch '^(\?\?|!!) ') { return $true }
        return -not (Test-DisposableWorktreePath -Path $_.Substring(3))
    })
    if ($work.Count -gt 0) {
        Write-Host "Preserving dirty worktree $Label : $Worktree (uncommitted work)"
        foreach ($entry in $work) { Write-Host "  $entry" }
        return
    }

    if ($WhatIf) {
        Write-Host "sweep: WOULD remove $Worktree -- $Label"
        return
    }

    $pending = Register-PendingWorktreeRemoval -Repo $Repo -Worktree $Worktree -LockNames $LockNames
    if (Remove-WorktreeDirectory -Repo $Repo -Worktree $Worktree -Label $Label) {
        if ($pending) { Remove-Item -LiteralPath $pending -Force -ErrorAction SilentlyContinue }
        Write-Host "Removed worktree $Label : $Worktree"
    } elseif ($pending) {
        Write-Host "WARNING: could not fully remove $Worktree; Remove-MergedWorktrees.ps1 retries the pending removal"
    } else {
        Write-Host "WARNING: could not fully remove $Worktree"
    }
}

# Git for Windows follows a directory junction while deleting a worktree: it empties the
# target, then fails with "Directory not empty" on the link and leaves a remnant without
# its .git file. Package managers link node_modules packages with junctions, and a junction
# can also point into another checkout or a shared store. Delete each link itself, never its
# target, before any recursive delete. Returns whether no directory link remains.
function Remove-WorktreeDirectoryLinks {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Worktree,
        [string]$Label = ''
    )

    if (-not $IsWindows) { return $true }

    # Get-ChildItem does not recurse through links, so every match is a link to remove.
    $find = { @(Get-ChildItem -LiteralPath $Worktree -Directory -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue) }
    foreach ($link in (& $find)) {
        # DirectoryInfo.Delete() without recursion removes the reparse point only.
        try { $link.Delete() } catch { Write-Host "Could not detach directory link $($link.FullName): $_" }
    }

    $remaining = & $find
    if ($remaining.Count -gt 0) {
        Write-Host "WARNING: worktree $Label kept: $($remaining.Count) directory link(s) could not be detached, first $($remaining[0].FullName): $Worktree"
        return $false
    }
    return $true
}

# Deletes an already-vetted worktree directory and prunes its registration. Returns
# whether the directory is gone. Callers must have checked that its content is disposable.
function Remove-WorktreeDirectory {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$Worktree,
        [string]$Label = ''
    )

    if (-not (Remove-WorktreeDirectoryLinks -Worktree $Worktree -Label $Label)) { return $false }

    # Primary path: let git remove it. Double force also clears a stale setup lock and
    # deletes the remnant of an earlier, partially failed removal.
    git -C $Repo -c core.longpaths=true worktree remove --force --force $Worktree 2>$null | Out-Null

    # Fallback for long-path failures and remnants that Git no longer recognizes.
    if (Test-Path -LiteralPath $Worktree) {
        if (-not (Remove-WorktreeDirectoryLinks -Worktree $Worktree -Label $Label)) { return $false }
        # \\?\ disables Win32 path normalization, so forward slashes (git's output
        # format) are NOT translated — convert to backslashes or the delete no-ops.
        $removalPath = if ($IsWindows) { '\\?\' + ($Worktree -replace '/', '\') } else { $Worktree }
        Remove-Item -LiteralPath $removalPath -Recurse -Force -ErrorAction SilentlyContinue
    }

    git -C $Repo worktree prune 2>$null | Out-Null
    return -not (Test-Path -LiteralPath $Worktree)
}

# AGENT EVIDENCE
# Agents keep logs, reports and PR-body drafts for a work item under
# <main checkout>-evidence/<lock name>/, beside the worktree root, so nothing lands
# loose in the parent directory. Evidence matters only while its item is active.
function Get-AgentEvidenceRoot {
    param([Parameter(Mandatory)][string]$MainCheckout)

    return "$(($MainCheckout -replace '\\', '/').TrimEnd('/'))-evidence"
}

# Removes each top-level entry of the evidence root whose newest write is older than
# the retention window. Returns the number of entries removed (or that would be).
function Remove-StaleAgentEvidence {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][int]$RetentionDays,
        [switch]$WhatIf
    )

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { return 0 }
    $cutoff = [DateTime]::UtcNow.AddDays(-$RetentionDays)
    $removed = 0
    foreach ($entry in (Get-ChildItem -LiteralPath $Root -Force -ErrorAction SilentlyContinue)) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            Write-Host "sweep: skipping evidence entry that is a link: $($entry.FullName)"
            continue
        }
        $items = @($entry)
        if ($entry.PSIsContainer) {
            $items += @(Get-ChildItem -LiteralPath $entry.FullName -Recurse -Force -ErrorAction SilentlyContinue)
        }
        $newest = ($items | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
        if ($newest -ge $cutoff) { continue }
        if ($WhatIf) {
            Write-Host "sweep: WOULD remove stale evidence $($entry.FullName)"
            $removed++
            continue
        }
        if ($entry.PSIsContainer -and -not (Remove-WorktreeDirectoryLinks -Worktree $entry.FullName -Label '(evidence)')) { continue }
        Remove-Item -LiteralPath $entry.FullName -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $entry.FullName) {
            Write-Host "sweep: WARNING could not remove stale evidence $($entry.FullName)"
            continue
        }
        Write-Host "sweep: removed stale evidence $($entry.FullName)"
        $removed++
    }
    return $removed
}

# LOCK MARKERS
# AgentLocks.ps1 records a worktree's lock name in its config.worktree as
# agent.lockName, plus agent.lockWorktree = that worktree's path. `git worktree add`
# copies the source checkout's config.worktree into the new worktree, so a marker
# counts only in the worktree whose path it records.
function ConvertTo-LockMarkerPath([string]$Path) {
    return [IO.Path]::GetFullPath($Path.Trim()).TrimEnd('\', '/')
}

function Get-OwnedWorktreeLockMarker {
    param([Parameter(Mandatory)][string]$Worktree)

    $marker = git -C $Worktree config --worktree --get agent.lockName 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $marker) { return $null }
    $owner = git -C $Worktree config --worktree --get agent.lockWorktree 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $owner) { return $null }
    $top = git -C $Worktree rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $top) { return $null }
    if ((ConvertTo-LockMarkerPath $owner) -ne (ConvertTo-LockMarkerPath $top)) { return $null }
    return $marker.Trim()
}

# PENDING REMOVALS
# On Windows a directory cannot be deleted while any process has its current directory
# inside it, and build or test processes can hold files open briefly. Git then deletes
# part of the tree, including the .git file, and fails. The remnant no longer looks like
# a worktree, so neither release nor the merged-worktree sweep would recognize it again.
# Before deleting a vetted checkout, cleanup records it under the common Git directory.
# The record is removed after success, and Complete-PendingWorktreeRemovals retries the
# rest once the blocking process has exited.

function Get-PendingWorktreeRemovalDirectory {
    param([Parameter(Mandatory)][string]$Repo)

    $common = git -C $Repo rev-parse --path-format=absolute --git-common-dir 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $common) { return $null }
    return Join-Path $common.Trim() 'agent-pending-removals'
}

function Get-WorktreeAgentLockNames {
    param([Parameter(Mandatory)][string]$Worktree, [string]$Branch = '')

    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $marker = Get-OwnedWorktreeLockMarker -Worktree $Worktree
    if ($marker) { [void]$names.Add($marker) }
    foreach ($text in @((Split-Path -Path $Worktree -Leaf), $Branch)) {
        foreach ($match in [regex]::Matches([string]$text, '(?:^|[-/])((?:pr|issue)-\d+)(?=$|[-/])', 'IgnoreCase')) {
            [void]$names.Add($match.Groups[1].Value.ToLowerInvariant())
        }
    }
    return $names | Sort-Object -CaseSensitive
}

function Register-PendingWorktreeRemoval {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$Worktree,
        [string[]]$LockNames = @()
    )

    $directory = Get-PendingWorktreeRemovalDirectory -Repo $Repo
    if (-not $directory) { return $null }
    $path = [IO.Path]::GetFullPath($Worktree).TrimEnd([char[]]@('/', '\'))
    $gitDirectory = git -C $path rev-parse --absolute-git-dir 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $gitDirectory) { return $null }
    $branch = git -C $path symbolic-ref -q --short HEAD 2>$null
    $names = @(Get-WorktreeAgentLockNames -Worktree $path -Branch ([string]$branch)) + $LockNames

    $key = if ($IsWindows) { $path.ToLowerInvariant() } else { $path }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($key)))
    $record = Join-Path $directory ($hash.Substring(0, 16).ToLowerInvariant() + '.json')
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    [ordered]@{
        path         = $path
        gitDirectory = [IO.Path]::GetFullPath($gitDirectory.Trim())
        lockNames    = @($names | Where-Object { $_ } | Sort-Object -CaseSensitive -Unique)
        recordedAt   = [DateTimeOffset]::UtcNow.ToString('o')
    } | ConvertTo-Json -Compress | Set-Content -LiteralPath $record -NoNewline
    return $record
}

function Test-HasWorkNewerThan {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][DateTimeOffset]$Cutoff
    )

    foreach ($file in (Get-ChildItem -LiteralPath $Path -File -Recurse -Force -ErrorAction SilentlyContinue)) {
        $relative = [IO.Path]::GetRelativePath($Path, $file.FullName)
        if ($relative -eq '.git' -or (Test-DisposableWorktreePath -Path $relative)) { continue }
        if ($file.LastWriteTimeUtc -gt $Cutoff.UtcDateTime) { return $true }
    }
    return $false
}

# Reserves every recorded item lock through deletion, so a new owner cannot reuse the
# path midway. Returns $null when any lock is held or unavailable.
function Invoke-WithPendingRemovalLocks {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [AllowEmptyCollection()][string[]]$LockNames,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    $agentLocks = Join-Path $Repo 'scripts/AgentLocks.ps1'
    if ($LockNames.Count -gt 0 -and -not (Test-Path -LiteralPath $agentLocks -PathType Leaf)) { return $null }
    $owner = "worktree-cleanup-$([Guid]::NewGuid().ToString('N'))"
    $acquired = [Collections.Generic.List[string]]::new()
    try {
        foreach ($name in $LockNames) {
            # Acquire prints a token. Never forward it.
            $null = & pwsh -NoProfile -File $agentLocks acquire -LockName $name -OwnerId $owner 2>&1
            if ($LASTEXITCODE -ne 0) { return $null }
            $acquired.Add($name)
        }
        return (& $Action)
    }
    finally {
        foreach ($name in $acquired) {
            $null = & pwsh -NoProfile -File $agentLocks release -LockName $name -OwnerId $owner 2>&1
            if ($LASTEXITCODE -ne 0) { Write-Host "WARNING: could not release cleanup reservation $name." }
        }
    }
}

function Complete-PendingWorktreeRemovals {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [switch]$WhatIf
    )

    $directory = Get-PendingWorktreeRemovalDirectory -Repo $Repo
    if (-not $directory -or -not (Test-Path -LiteralPath $directory)) { return 0 }
    $completed = 0
    foreach ($recordFile in (Get-ChildItem -LiteralPath $directory -File -Filter '*.json')) {
        try {
            $record = Get-Content -LiteralPath $recordFile.FullName -Raw | ConvertFrom-Json
            $path = [string]$record.path
            $recordedAt = [DateTimeOffset]::Parse([string]$record.recordedAt, [Globalization.CultureInfo]::InvariantCulture)
        }
        catch {
            Write-Host "sweep: WARNING unreadable pending removal record: $($recordFile.FullName)"
            continue
        }
        $drop = { if (-not $WhatIf) { Remove-Item -LiteralPath $recordFile.FullName -Force -ErrorAction SilentlyContinue } }

        if (-not $path -or -not (Test-Path -LiteralPath $path)) { & $drop; continue }

        # A new checkout at the same path has its own Git metadata, created after the record.
        $marker = Join-Path $path '.git'
        if (Test-Path -LiteralPath $marker -PathType Leaf) {
            $gitDirectory = ((Get-Content -LiteralPath $marker -TotalCount 1) -replace '^gitdir:\s*', '').Trim()
            if ($gitDirectory -and -not [IO.Path]::IsPathRooted($gitDirectory)) { $gitDirectory = Join-Path $path $gitDirectory }
            if ($gitDirectory -and (Test-Path -LiteralPath $gitDirectory) -and (
                    [IO.Path]::GetFullPath($gitDirectory).TrimEnd([char[]]@('/', '\')) -ne ([string]$record.gitDirectory).TrimEnd([char[]]@('/', '\')) -or
                    (Get-Item -LiteralPath $gitDirectory -Force).CreationTimeUtc -gt $recordedAt.UtcDateTime)) {
                Write-Host "sweep: dropping pending removal superseded by a new checkout: $path"
                & $drop
                continue
            }
        }
        elseif (Test-Path -LiteralPath $marker) {
            Write-Host "sweep: dropping pending removal for a standalone repository: $path"
            & $drop
            continue
        }

        # The checkout was vetted before deletion began. Anything written since is new work.
        if (Test-HasWorkNewerThan -Path $path -Cutoff $recordedAt) {
            Write-Host "sweep: WARNING preserving pending removal with files changed after removal began; inspect manually: $path"
            & $drop
            continue
        }

        if ($WhatIf) {
            Write-Host "sweep: WOULD finish pending removal of $path"
            continue
        }
        $removed = Invoke-WithPendingRemovalLocks -Repo $Repo -LockNames @($record.lockNames) -Action {
            Remove-WorktreeDirectory -Repo $Repo -Worktree $path -Label '(pending removal)'
        }
        if ($null -eq $removed) {
            Write-Host "sweep: preserving pending removal while its lock is held or unavailable: $path"
        } elseif ($removed) {
            & $drop
            $completed++
            Write-Host "sweep: finished pending removal of $path"
        } else {
            Write-Host "sweep: WARNING pending removal is still blocked, retrying next sweep: $path"
        }
    }
    return $completed
}
