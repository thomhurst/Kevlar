[CmdletBinding()]
param(
    [string]$WorkflowPath = (Join-Path (Split-Path -Parent $PSScriptRoot) '.github/workflows/ci.yml')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$workflow = Get-Content -LiteralPath $WorkflowPath -Raw
$changelogStepStart = $workflow.IndexOf('- name: Verify changelog and release notes', [StringComparison]::Ordinal)
$changelogStepEnd = if ($changelogStepStart -lt 0)
{
    -1
}
else
{
    $workflow.IndexOf('- name:', $changelogStepStart + 1, [StringComparison]::Ordinal)
}
if ($changelogStepStart -lt 0 -or $changelogStepEnd -lt 0)
{
    throw "Changelog verification step was not found in '$WorkflowPath'."
}

$changelogStep = $workflow.Substring($changelogStepStart, $changelogStepEnd - $changelogStepStart)
foreach ($requiredText in @(
    'RELEASE_VERSION: ${{ steps.gitversion.outputs.semVer }}',
    './scripts/Get-ReleaseNotes.ps1 -Version $env:RELEASE_VERSION'))
{
    if (-not $changelogStep.Contains($requiredText, [StringComparison]::Ordinal))
    {
        throw "Changelog verification is missing '$requiredText'."
    }
}

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
Assert-Contains 'Retry-safe release tag' './scripts/Push-ReleaseTag.ps1'
Assert-Contains 'Retry-safe NuGet publication' './scripts/Push-NuGetRelease.ps1'
Assert-Contains 'Retry-safe GitHub release' './scripts/Publish-GitHubRelease.ps1'

$packageVerificationScript = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Verify-Packages.ps1') -Raw
foreach ($requiredVerification in @('Verify-ReleaseApiBaselines.ps1', 'Verify-PublicApi.ps1'))
{
    if (-not $packageVerificationScript.Contains($requiredVerification, [StringComparison]::Ordinal))
    {
        throw "Package verification must invoke '$requiredVerification' for stable releases."
    }
}

Assert-StepGuarded 'Create and push release tag'
Assert-StepGuarded 'NuGet login'
Assert-StepGuarded 'Push to NuGet'
Assert-StepGuarded 'Create GitHub release'

if ($publish.Contains('--skip-duplicate', [StringComparison]::Ordinal))
{
    throw 'Publish must verify an existing package payload instead of masking duplicates.'
}

if ($publish.Contains('--generate-notes', [StringComparison]::Ordinal))
{
    throw 'Publish must use CHANGELOG.md notes instead of generated notes.'
}

$nugetPublishScript = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Push-NuGetRelease.ps1') -Raw
foreach ($requiredText in @('Compare-NuGetPackagePayload.ps1', '--no-symbols', "-Filter '*.snupkg'"))
{
    if (-not $nugetPublishScript.Contains($requiredText, [StringComparison]::Ordinal))
    {
        throw "NuGet publication is missing '$requiredText'."
    }
}

if ($nugetPublishScript.Contains('--skip-duplicate', [StringComparison]::Ordinal))
{
    throw 'NuGet publication must reject conflicting duplicates after comparing payloads.'
}

. (Join-Path $PSScriptRoot 'PackageDependencyPolicy.ps1')
Assert-ShippedDependencyFloor `
    -DependencyId 'Microsoft.Extensions.Options' `
    -DependencyVersion '8.0.2' `
    -Context 'fixture'
$raisedFloorRejected = $false
try
{
    Assert-ShippedDependencyFloor `
        -DependencyId 'Microsoft.Extensions.Options' `
        -DependencyVersion '10.0.11' `
        -Context 'fixture'
}
catch
{
    $raisedFloorRejected = $_.Exception.Message -match 'major version 8 or earlier'
}

if (-not $raisedFloorRejected)
{
    throw 'Shipped dependency policy accepted a Microsoft.Extensions 10.x floor.'
}

$tagIndex = $publish.IndexOf('- name: Create and push release tag', [StringComparison]::Ordinal)
$packageIndex = $publish.IndexOf('- name: Push to NuGet', [StringComparison]::Ordinal)
$releaseIndex = $publish.IndexOf('- name: Create GitHub release', [StringComparison]::Ordinal)
if ($tagIndex -lt 0 -or $packageIndex -lt 0 -or $releaseIndex -lt 0 -or
    $packageIndex -ge $tagIndex -or $tagIndex -ge $releaseIndex)
{
    throw 'Release ordering must be NuGet push, tag, then GitHub release.'
}

$githubReleaseScript = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Publish-GitHubRelease.ps1') -Raw
foreach ($requiredText in @('gh release create', '--verify-tag', '--notes-file', 'gh release upload', '--clobber'))
{
    if (-not $githubReleaseScript.Contains($requiredText, [StringComparison]::Ordinal))
    {
        throw "GitHub release publication is missing '$requiredText'."
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "kevlar-release-notes-$([Guid]::NewGuid().ToString('N'))"
$resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
[IO.Directory]::CreateDirectory($resolvedTemporaryRoot) | Out-Null

try
{
    $apiFixtureRoot = Join-Path $resolvedTemporaryRoot 'api-baselines'
    $apiFixtureSource = Join-Path $apiFixtureRoot 'src/Fixture'
    [IO.Directory]::CreateDirectory($apiFixtureSource) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $apiFixtureRoot 'src/Kevlar.Analyzers')) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $apiFixtureSource 'PublicAPI.Unshipped.txt'),
        "#nullable enable`nFixture.NewApi`n",
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $apiFixtureRoot 'src/Kevlar.Analyzers/AnalyzerReleases.Unshipped.md'),
        "; Unshipped analyzer release`n",
        [Text.UTF8Encoding]::new($false))

    $baselineScript = Join-Path $PSScriptRoot 'Verify-ReleaseApiBaselines.ps1'
    & $baselineScript -Version '1.0.0-preview.1' -RepositoryRoot $apiFixtureRoot

    $unfrozenRejected = $false
    try
    {
        & $baselineScript -Version '1.0.0' -RepositoryRoot $apiFixtureRoot
    }
    catch
    {
        $unfrozenRejected = $true
    }

    if (-not $unfrozenRejected)
    {
        throw 'Stable release verification accepted an unshipped public API.'
    }

    [IO.File]::WriteAllText(
        (Join-Path $apiFixtureSource 'PublicAPI.Unshipped.txt'),
        "#nullable enable`n",
        [Text.UTF8Encoding]::new($false))
    & $baselineScript -Version '1.0.0' -RepositoryRoot $apiFixtureRoot

    $fixturePath = Join-Path $resolvedTemporaryRoot 'CHANGELOG.md'
    $outputPath = Join-Path $resolvedTemporaryRoot 'notes.md'
    [IO.File]::WriteAllText(
        $fixturePath,
        "# Changelog`n`n## [Unreleased]`n`n## [1.2.3] - 2026-08-25`n`n### Fixed`n`n- Release note.`n`n[Unreleased]: https://example.invalid/compare/v1.2.3...HEAD`n[1.2.3]: https://example.invalid/compare/v1.2.2...v1.2.3`n",
        [Text.UTF8Encoding]::new($false))

    & (Join-Path $PSScriptRoot 'Get-ReleaseNotes.ps1') `
        -Version '1.2.3-pr.274.75' `
        -ChangelogPath $fixturePath `
        -OutputPath $outputPath

    $actualNotes = (Get-Content -LiteralPath $outputPath -Raw).Trim() -replace '\r\n?', "`n"
    $expectedNotes = "### Fixed`n`n- Release note."
    if ($actualNotes -ne $expectedNotes)
    {
        throw "Release-note extraction mismatch: '$actualNotes'."
    }

    [IO.File]::WriteAllText(
        $fixturePath,
        "# Changelog`n`n## Unreleased`n`n### Added`n`n- Pending release note.`n",
        [Text.UTF8Encoding]::new($false))

    & (Join-Path $PSScriptRoot 'Get-ReleaseNotes.ps1') `
        -Version '2.0.0-alpha.1+build.42' `
        -ChangelogPath $fixturePath `
        -OutputPath $outputPath

    $actualNotes = (Get-Content -LiteralPath $outputPath -Raw).Trim() -replace '\r\n?', "`n"
    $expectedNotes = "### Added`n`n- Pending release note."
    if ($actualNotes -ne $expectedNotes)
    {
        throw "Unreleased-note extraction mismatch: '$actualNotes'."
    }

    function New-PackageFixture([string]$path, [string]$payload, [bool]$includeSignature)
    {
        $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
        try
        {
            $payloadEntry = $archive.CreateEntry('lib/net8.0/example.dll')
            $writer = [IO.StreamWriter]::new($payloadEntry.Open())
            try
            {
                $writer.Write($payload)
            }
            finally
            {
                $writer.Dispose()
            }

            if ($includeSignature)
            {
                $signatureEntry = $archive.CreateEntry('.signature.p7s')
                $writer = [IO.StreamWriter]::new($signatureEntry.Open())
                try
                {
                    $writer.Write('repository signature')
                }
                finally
                {
                    $writer.Dispose()
                }
            }
        }
        finally
        {
            $archive.Dispose()
        }
    }

    $expectedPackage = Join-Path $resolvedTemporaryRoot 'expected.nupkg'
    $matchingPackage = Join-Path $resolvedTemporaryRoot 'matching.nupkg'
    $conflictingPackage = Join-Path $resolvedTemporaryRoot 'conflicting.nupkg'
    New-PackageFixture $expectedPackage 'same payload' $false
    New-PackageFixture $matchingPackage 'same payload' $true
    New-PackageFixture $conflictingPackage 'different payload' $true

    $comparePackageScript = Join-Path $PSScriptRoot 'Compare-NuGetPackagePayload.ps1'
    & $comparePackageScript -ExpectedPath $expectedPackage -ActualPath $matchingPackage

    $conflictRejected = $false
    try
    {
        & $comparePackageScript -ExpectedPath $expectedPackage -ActualPath $conflictingPackage
    }
    catch
    {
        $conflictRejected = $true
    }

    if (-not $conflictRejected)
    {
        throw 'NuGet payload verification accepted conflicting package contents.'
    }

    $remotePath = Join-Path $resolvedTemporaryRoot 'remote.git'
    $workPath = Join-Path $resolvedTemporaryRoot 'work'
    & git init --bare $remotePath | Out-Null
    & git init $workPath | Out-Null
    & git -C $workPath config user.email 'verification@example.invalid'
    & git -C $workPath config user.name 'Release Verification'
    [IO.File]::WriteAllText((Join-Path $workPath 'file.txt'), 'first', [Text.UTF8Encoding]::new($false))
    & git -C $workPath add file.txt
    & git -C $workPath commit -m 'first' | Out-Null
    & git -C $workPath remote add origin $remotePath
    $firstCommit = (& git -C $workPath rev-parse HEAD).Trim()

    $pushTagScript = Join-Path $PSScriptRoot 'Push-ReleaseTag.ps1'
    & $pushTagScript -Version '1.2.3' -Commit $firstCommit -WorkingDirectory $workPath
    & $pushTagScript -Version '1.2.3' -Commit $firstCommit -WorkingDirectory $workPath

    [IO.File]::WriteAllText((Join-Path $workPath 'file.txt'), 'second', [Text.UTF8Encoding]::new($false))
    & git -C $workPath add file.txt
    & git -C $workPath commit -m 'second' | Out-Null
    $secondCommit = (& git -C $workPath rev-parse HEAD).Trim()

    $mismatchRejected = $false
    try
    {
        & $pushTagScript -Version '1.2.3' -Commit $secondCommit -WorkingDirectory $workPath
    }
    catch
    {
        $mismatchRejected = $true
    }

    if (-not $mismatchRejected)
    {
        throw 'Release tag verification accepted an existing tag on a different commit.'
    }
}
finally
{
    if ($resolvedTemporaryRoot.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase))
    {
        Get-ChildItem -LiteralPath $resolvedTemporaryRoot -Recurse -Force | ForEach-Object {
            $_.Attributes = [IO.FileAttributes]::Normal
        }
        [IO.Directory]::Delete($resolvedTemporaryRoot, $true)
    }
}

Write-Host 'Release workflow guard, ordering, artifacts, dry run, and notes passed.'
