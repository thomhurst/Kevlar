#!/usr/bin/env pwsh
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

$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Assert-FileContains([string]$Name, [string]$Path, [string[]]$Texts)
{
    $content = Get-Content -LiteralPath (Join-Path $repositoryRoot $Path) -Raw
    foreach ($text in $Texts)
    {
        if (-not $content.Contains($text, [StringComparison]::Ordinal))
        {
            throw "$Name ($Path) is missing '$text'."
        }
    }
}

# Releases are published from the draft that release-draft.yml keeps for main; GitHub creates the
# tag and the release, so the publish job only pushes and attaches the packages.
if (-not [regex]::IsMatch($workflow, '(?m)^  release:\r?\n    types: \[published\]\r?$'))
{
    throw 'CI must run on published releases.'
}

Assert-Contains 'Publish release guard' "if: github.event_name == 'release'"
Assert-Contains 'Publish approval environment' 'environment: nuget'
Assert-Contains 'Release version and main ancestry' 'uses: ./.github/actions/resolve-version'
Assert-Contains 'Retry-safe NuGet publication' './scripts/Push-NuGetRelease.ps1'
Assert-Contains 'Retry-safe release assets' 'gh release upload "$RELEASE_TAG" packages/*.nupkg packages/*.snupkg --clobber'
Assert-Contains 'Derived publish package set' '$expectedPackageIds = @(./scripts/Get-PackablePackageIds.ps1)'
Assert-Contains 'Release-versioned packages' 'Packages must carry release version'

foreach ($retired in @('Push-ReleaseTag.ps1', 'Publish-GitHubRelease.ps1', 'release_dry_run', 'publish_nuget'))
{
    if ($workflow.Contains($retired, [StringComparison]::Ordinal))
    {
        throw "CI must not use '$retired': publishing the draft release creates the tag and the release."
    }
}

Assert-FileContains 'Release version resolution' '.github/actions/resolve-version/action.yml' @(
    "if: github.event_name != 'release'",
    'git merge-base --is-ancestor "$GITHUB_SHA" origin/main',
    'bash "$GITHUB_ACTION_PATH/release-version.sh" "$RELEASE_TAG"')
Assert-FileContains 'Draft release' '.github/workflows/release-draft.yml' @(
    "DRAFT_MARKER: '<!-- kevlar-release-draft -->'",
    'releases/generate-notes',
    'previous_tag_name="$latest"',
    'target_commitish="$GITHUB_SHA"',
    '-F draft=true',
    "if: github.ref == 'refs/heads/main'",
    'git ls-remote --exit-code --tags origin "refs/tags/$existing"',
    'bash "$versions" --greater "$current_tag" "$tag"',
    '-f target_commitish="$GITHUB_SHA" -f body="$body" --jq .html_url)',
    'select(.draft and ((.body // "") | contains(env.DRAFT_MARKER)))')

# Release tag grammar: optional v, SemVer 2.0 without build metadata, parts at most 65534.
$releaseVersionScript = Join-Path $repositoryRoot '.github/actions/resolve-version/release-version.sh'
$acceptedTags = [ordered]@{
    'v1.2.3' = '1.2.3'
    '1.2.3' = '1.2.3'
    'v0.0.0' = '0.0.0'
    'v1.2.3-beta.1' = '1.2.3-beta.1'
    'v1.2.3-beta01' = '1.2.3-beta01'
    'v1.2.3-0' = '1.2.3-0'
    'v65534.65534.65534' = '65534.65534.65534'
}
foreach ($entry in $acceptedTags.GetEnumerator())
{
    $resolved = (& bash $releaseVersionScript $entry.Key 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $resolved -ne $entry.Value)
    {
        throw "Release tag '$($entry.Key)' should resolve to '$($entry.Value)'; got exit $LASTEXITCODE '$resolved'."
    }
}

foreach ($tag in @('v1.02.3', 'v01.2.3', 'v1.2.3-beta..1', 'v1.2.3-beta.01', 'v1.2.3-01', 'v1.2.3-', 'v1.2',
    'v65535.0.0', 'v1.2.65535', 'release-1', 'v1.2.3+build.1', ''))
{
    & bash $releaseVersionScript $tag *> $null
    if ($LASTEXITCODE -eq 0)
    {
        throw "Release tag '$tag' should be rejected."
    }
}

# SemVer 2.0 precedence, used to keep a higher version the owner set on the draft release.
foreach ($pair in @(
    @('1.11.0', '1.10.1'),
    @('1.10.0', '1.9.0'),
    @('v2.0.0-rc.10', 'v2.0.0-rc.9'),
    @('2.0.0', '2.0.0-rc.1'),
    @('1.0.0-alpha.1', '1.0.0-alpha'),
    @('1.0.0-beta', '1.0.0-alpha.1'),
    @('1.0.0-alpha', '1.0.0-1'),
    @('1.0.0-alpha.beta', '1.0.0-alpha.1'),
    @('1.0.0-beta.11', '1.0.0-beta.2'),
    @('1.0.0-a', '1.0.0-B')))
{
    & bash $releaseVersionScript --greater $pair[0] $pair[1] *> $null
    if ($LASTEXITCODE -ne 0)
    {
        throw "'$($pair[0])' should rank above '$($pair[1])'."
    }

    & bash $releaseVersionScript --greater $pair[1] $pair[0] *> $null
    if ($LASTEXITCODE -eq 0)
    {
        throw "'$($pair[1])' should not rank above '$($pair[0])'."
    }
}

foreach ($pair in @(@('1.2.3', '1.2.3'), @('v1.2.3', '1.2.3'), @('1.0.0-rc.1', 'v1.0.0-rc.1'), @('bogus', '1.0.0'), @('v1.02.0', '1.0.0')))
{
    & bash $releaseVersionScript --greater $pair[0] $pair[1] *> $null
    if ($LASTEXITCODE -eq 0)
    {
        throw "'$($pair[0])' should not rank above '$($pair[1])'."
    }
}

$packageVerificationScript = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Verify-Packages.ps1') -Raw
foreach ($requiredVerification in @('Verify-ReleaseApiBaselines.ps1', 'Verify-PublicApi.ps1'))
{
    if (-not $packageVerificationScript.Contains($requiredVerification, [StringComparison]::Ordinal))
    {
        throw "Package verification must invoke '$requiredVerification' for stable releases."
    }
}

$publicApiVerificationScript = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Verify-PublicApi.ps1') -Raw
foreach ($requiredText in @(
    '^analyzers/dotnet/(?:roslyn[\d.]+/)?cs/(Kevlar\.(?:Analyzers|CodeFixes))\.dll$',
    'Expected package assemblies were not found'))
{
    if (-not $publicApiVerificationScript.Contains($requiredText, [StringComparison]::Ordinal))
    {
        throw "Public API package verification is missing '$requiredText'."
    }
}

$dependencyTablePattern = [regex]'(?ms)^\$expectedDependencies = @\{(?<body>.*?)^\}\r?$'
$dependencyTableMatch = $dependencyTablePattern.Match($packageVerificationScript)
if (-not $dependencyTableMatch.Success)
{
    throw 'Verify-Packages.ps1 expected dependency table was not found.'
}

$dependencyPackageIds = @(
    [regex]::Matches(
        $dependencyTableMatch.Groups['body'].Value,
        "(?m)^    '(?<id>Kevlar(?:\.[^']+)?)' = @\{") |
        ForEach-Object { $_.Groups['id'].Value } |
        Sort-Object)
$packablePackageIds = @(& (Join-Path $PSScriptRoot 'Get-PackablePackageIds.ps1'))
if (($dependencyPackageIds -join ',') -ne ($packablePackageIds -join ','))
{
    throw (
        "Packable project IDs must match Verify-Packages.ps1: " +
        "projects=[$($packablePackageIds -join ', ')], table=[$($dependencyPackageIds -join ', ')].")
}

if ($publish.Contains('--skip-duplicate', [StringComparison]::Ordinal))
{
    throw 'Publish must verify an existing package payload instead of masking duplicates.'
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

$verifyIndex = $publish.IndexOf('- name: Verify publish artifacts', [StringComparison]::Ordinal)
$loginIndex = $publish.IndexOf('- name: NuGet login', [StringComparison]::Ordinal)
$packageIndex = $publish.IndexOf('- name: Push to NuGet', [StringComparison]::Ordinal)
$assetIndex = $publish.IndexOf('- name: Attach packages to release', [StringComparison]::Ordinal)
if ($verifyIndex -lt 0 -or $loginIndex -lt 0 -or $packageIndex -lt 0 -or $assetIndex -lt 0 -or
    $verifyIndex -ge $loginIndex -or $loginIndex -ge $packageIndex -or $packageIndex -ge $assetIndex)
{
    throw 'Release ordering must be artifact verification, NuGet login, NuGet push, then release assets.'
}

$releaseConfiguration = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) '.github/release.yml') -Raw
# GitHub uses the first matching category, so the order is part of the contract: Breaking Changes
# first, Dependencies last, and every category in between excludes `dependencies`.
$expectedCategories = @('Breaking Changes', 'Features', 'Fixes', 'Performance', 'Documentation', 'Other Changes', 'Dependencies')
$categories = @([regex]::Matches($releaseConfiguration, '(?m)^    - title: (?<title>.+?)\r?$') | ForEach-Object { $_.Groups['title'].Value })
if (($categories -join ',') -ne ($expectedCategories -join ','))
{
    throw "Generated release-note categories must be [$($expectedCategories -join ', ')]; got [$($categories -join ', ')]."
}

$categoryBlocks = [regex]::Split($releaseConfiguration, '(?m)^    - title: ')
foreach ($block in $categoryBlocks[2..($categoryBlocks.Count - 2)])
{
    if ($block -notmatch '(?ms)^      exclude:\r?\n        labels:\r?\n(?:          - .+\r?\n)*?          - dependencies\r?$')
    {
        throw "Release-note category '$(($block -split '\r?\n')[0])' must exclude the dependencies label."
    }
}

if ($releaseConfiguration -notmatch '(?ms)^  exclude:\r?\n    labels:\r?\n(?:      - .+\r?\n)*?      - ignore-for-release\r?$')
{
    throw 'Generated release notes must exclude the ignore-for-release label.'
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "kevlar-release-workflow-$([Guid]::NewGuid().ToString('N'))"
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

    # A release run inherits GITHUB_REF=refs/tags/<tag>, which makes every version a tag build.
    # Pin the ref for each fixture so the branch cases still skip the freeze on a release run.
    $originalGitHubRef = $env:GITHUB_REF
    try
    {
        $env:GITHUB_REF = 'refs/heads/verification'
        & $baselineScript -Version '1.0.0-preview.1' -RepositoryRoot $apiFixtureRoot

        # A prerelease published from a tag, with or without the v prefix, must be frozen too.
        foreach ($tagRef in @('refs/tags/v1.0.0-preview.1', 'refs/tags/1.0.0-preview.1'))
        {
            $env:GITHUB_REF = $tagRef
            $tagBuildRejected = $false
            try
            {
                & $baselineScript -Version '1.0.0-preview.1' -RepositoryRoot $apiFixtureRoot
            }
            catch
            {
                $tagBuildRejected = $true
            }

            if (-not $tagBuildRejected)
            {
                throw "Tag build $tagRef accepted an unshipped public API."
            }
        }

        $env:GITHUB_REF = 'refs/heads/verification'
    }
    finally
    {
        $env:GITHUB_REF = $originalGitHubRef
    }

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

    function New-PackageFixture([string]$path, [string]$payload, [bool]$includeSignature)
    {
        $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
        try
        {
            $payloadEntry = $archive.CreateEntry('lib/net10.0/example.dll')
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

Write-Host 'Release workflow trigger, guards, ordering, artifacts, and draft release passed.'
