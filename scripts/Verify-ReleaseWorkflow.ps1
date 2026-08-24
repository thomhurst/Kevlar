[CmdletBinding()]
param(
    [string]$WorkflowPath = (Join-Path (Split-Path -Parent $PSScriptRoot) '.github/workflows/ci.yml')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$workflow = Get-Content -LiteralPath $WorkflowPath -Raw
$publishStart = $workflow.IndexOf("`n  publish:", [StringComparison]::Ordinal)
if ($publishStart -lt 0)
{
    throw "Publish job was not found in '$WorkflowPath'."
}

$publish = $workflow.Substring($publishStart)

function Assert-Contains([string]$Name, [string]$Text)
{
    if (-not $publish.Contains($Text, [StringComparison]::Ordinal))
    {
        throw "$Name is missing '$Text'."
    }
}

function Assert-StepGuarded([string]$StepName)
{
    $pattern = "(?m)^      - name: $([regex]::Escape($StepName))\r?\n        if: inputs\.release_dry_run != true\r?$"
    if (-not [regex]::IsMatch($publish, $pattern))
    {
        throw "$StepName must be disabled during a release dry run."
    }
}

Assert-Contains 'Publish branch guard' "github.ref == 'refs/heads/main'"
Assert-Contains 'Publish approval environment' 'environment: nuget'
Assert-Contains 'Changelog release notes' './scripts/Get-ReleaseNotes.ps1'
Assert-Contains 'GitHub release notes file' '--notes-file "$RELEASE_NOTES_PATH"'
Assert-Contains 'Release tag verification' '--verify-tag'
Assert-Contains 'NuGet package push' 'dotnet nuget push packages/*.nupkg'
Assert-Contains 'Separate symbol publishing' '--no-symbols'
Assert-Contains 'NuGet symbol package push' 'dotnet nuget push packages/*.snupkg'

Assert-StepGuarded 'Create and push release tag'
Assert-StepGuarded 'NuGet login'
Assert-StepGuarded 'Push to NuGet'
Assert-StepGuarded 'Create GitHub release'

if ($publish.Contains('--skip-duplicate', [StringComparison]::Ordinal))
{
    throw 'Publish must not mask duplicate-package failures with --skip-duplicate.'
}

if ($publish.Contains('--generate-notes', [StringComparison]::Ordinal))
{
    throw 'Publish must use CHANGELOG.md notes instead of generated notes.'
}

$tagIndex = $publish.IndexOf('- name: Create and push release tag', [StringComparison]::Ordinal)
$packageIndex = $publish.IndexOf('- name: Push to NuGet', [StringComparison]::Ordinal)
$releaseIndex = $publish.IndexOf('- name: Create GitHub release', [StringComparison]::Ordinal)
if ($tagIndex -lt 0 -or $packageIndex -lt 0 -or $releaseIndex -lt 0 -or
    $tagIndex -ge $packageIndex -or $packageIndex -ge $releaseIndex)
{
    throw 'Release ordering must be tag, NuGet push, then GitHub release.'
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "kevlar-release-notes-$([Guid]::NewGuid().ToString('N'))"
$resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
[IO.Directory]::CreateDirectory($resolvedTemporaryRoot) | Out-Null

try
{
    $fixturePath = Join-Path $resolvedTemporaryRoot 'CHANGELOG.md'
    $outputPath = Join-Path $resolvedTemporaryRoot 'notes.md'
    [IO.File]::WriteAllText(
        $fixturePath,
        "# Changelog`n`n## [Unreleased]`n`n## [1.2.3] - 2026-08-25`n`n### Fixed`n`n- Release note.`n`n[Unreleased]: https://example.invalid/compare/v1.2.3...HEAD`n[1.2.3]: https://example.invalid/compare/v1.2.2...v1.2.3`n",
        [Text.UTF8Encoding]::new($false))

    & (Join-Path $PSScriptRoot 'Get-ReleaseNotes.ps1') `
        -Version '1.2.3' `
        -ChangelogPath $fixturePath `
        -OutputPath $outputPath

    $actualNotes = (Get-Content -LiteralPath $outputPath -Raw).Trim() -replace '\r\n?', "`n"
    $expectedNotes = "### Fixed`n`n- Release note."
    if ($actualNotes -ne $expectedNotes)
    {
        throw "Release-note extraction mismatch: '$actualNotes'."
    }
}
finally
{
    if ($resolvedTemporaryRoot.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase))
    {
        [IO.Directory]::Delete($resolvedTemporaryRoot, $true)
    }
}

Write-Host 'Release workflow guard, ordering, artifacts, dry run, and notes passed.'
