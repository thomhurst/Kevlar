[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$isStableVersion = $Version -match '^\d+\.\d+\.\d+$'
$isTagBuild = $env:GITHUB_REF -match '^refs/tags/v'
if (-not $isStableVersion -and -not $isTagBuild)
{
    Write-Host "API baseline freeze skipped for prerelease version '$Version'."
    return
}

$sourceRoot = Join-Path $RepositoryRoot 'src'
$failures = [Collections.Generic.List[string]]::new()
$unshippedFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter 'PublicAPI.Unshipped*.txt')
if ($unshippedFiles.Count -eq 0)
{
    $failures.Add('No PublicAPI.Unshipped*.txt files were found.')
}

foreach ($file in $unshippedFiles)
{
    $contentLines = @(
        Get-Content -LiteralPath $file.FullName |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_.Length -gt 0 })
    if ($contentLines.Count -ne 1 -or $contentLines[0] -ne '#nullable enable')
    {
        $relativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName)
        $failures.Add("$relativePath must contain only '#nullable enable'.")
    }
}

$analyzerUnshippedPath = Join-Path $sourceRoot 'Kevlar.Analyzers/AnalyzerReleases.Unshipped.md'
if (-not (Test-Path -LiteralPath $analyzerUnshippedPath -PathType Leaf))
{
    $failures.Add('src/Kevlar.Analyzers/AnalyzerReleases.Unshipped.md was not found.')
}
elseif (Select-String -LiteralPath $analyzerUnshippedPath -Pattern '^KEV\d+\s*\|' -Quiet)
{
    $failures.Add('AnalyzerReleases.Unshipped.md still contains rule rows.')
}

if ($failures.Count -gt 0)
{
    throw "Stable release API baselines are not frozen:`n- $($failures -join "`n- ")"
}

Write-Host "Stable release API baselines are frozen for $Version."
