[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$Commit,

    [string]$Remote = 'origin',

    [string]$WorkingDirectory = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$tag = "v$Version"
$tagRef = "refs/tags/$tag"
$resolvedCommit = (& git -C $WorkingDirectory rev-parse --verify "$Commit^{commit}").Trim()
if ($LASTEXITCODE -ne 0)
{
    throw "Commit '$Commit' could not be resolved."
}

$remoteRefs = @(& git -C $WorkingDirectory ls-remote --tags $Remote $tagRef "$tagRef^{}")
if ($LASTEXITCODE -ne 0)
{
    throw "Could not inspect $tagRef on remote '$Remote'."
}

if ($remoteRefs.Count -gt 0)
{
    $peeled = $remoteRefs | Where-Object { $_ -match "\s$([regex]::Escape($tagRef))\^\{\}$" } | Select-Object -First 1
    $target = if ($peeled) { $peeled } else { $remoteRefs[0] }
    $remoteCommit = ($target -split '\s+', 2)[0]
    if (-not $remoteCommit.Equals($resolvedCommit, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "$tagRef already targets $remoteCommit instead of $resolvedCommit."
    }

    Write-Host "$tagRef already targets $resolvedCommit; nothing to do."
    exit 0
}

$localCommit = (& git -C $WorkingDirectory rev-parse --verify "$tagRef^{commit}" 2>$null)
if ($LASTEXITCODE -eq 0)
{
    $localCommit = $localCommit.Trim()
    if (-not $localCommit.Equals($resolvedCommit, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Local $tagRef targets $localCommit instead of $resolvedCommit."
    }
}
else
{
    & git -C $WorkingDirectory tag $tag $resolvedCommit
    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not create $tagRef."
    }
}

& git -C $WorkingDirectory push $Remote $tagRef
if ($LASTEXITCODE -ne 0)
{
    throw "Could not push $tagRef to remote '$Remote'."
}
