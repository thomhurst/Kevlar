[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Tag,

    [Parameter(Mandatory)]
    [string]$AssetsPath,

    [string]$TargetCommitish = 'HEAD',

    [string]$Headline,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-PreviousReleaseTag
{
    $currentVersion = [Version]$Tag.Substring(1)
    $tags = @(& git tag --list 'v*' --sort=-v:refname)
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Reading release tags failed.'
    }

    $previousTag = $tags |
        Where-Object {
            $_ -match '^v(?<version>[0-9]+\.[0-9]+\.[0-9]+)$' -and
            [Version]$Matches['version'] -lt $currentVersion
        } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($previousTag))
    {
        throw "No stable release tag precedes '$Tag'."
    }

    return $previousTag
}

function Get-GeneratedReleaseBody([string]$PreviousTag)
{
    $generatedNotes = & gh api --method POST 'repos/{owner}/{repo}/releases/generate-notes' `
        -f "tag_name=$Tag" `
        -f "target_commitish=$TargetCommitish" `
        -f "previous_tag_name=$PreviousTag" `
        --jq '.body'
    if ($LASTEXITCODE -ne 0)
    {
        throw "Generating GitHub release notes for '$Tag' failed."
    }

    $body = ($generatedNotes -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($body))
    {
        throw "GitHub generated empty release notes for '$Tag'."
    }

    if (-not [string]::IsNullOrWhiteSpace($Headline))
    {
        $body = "$($Headline.Trim())$([Environment]::NewLine)$([Environment]::NewLine)$body"
    }

    return $body
}

$resolvedAssetsPath = (Resolve-Path -LiteralPath $AssetsPath).Path
$assets = @(Get-ChildItem -LiteralPath $resolvedAssetsPath -File |
    Where-Object Extension -in '.nupkg', '.snupkg' |
    Sort-Object Name |
    ForEach-Object FullName)
if ($assets.Count -eq 0)
{
    throw "No release assets were found in '$resolvedAssetsPath'."
}

$previousTag = Get-PreviousReleaseTag
if ($Tag -eq 'v1.0.0' -and [string]::IsNullOrWhiteSpace($Headline))
{
    $Headline = 'Kevlar 1.0 establishes the stable Shield API. See [Upgrading from 0.x](https://thomhurst.github.io/Kevlar/docs/upgrading).'
}

if ($DryRun)
{
    $body = Get-GeneratedReleaseBody $previousTag
    Write-Host "Generated release notes preview for $Tag (after $previousTag):"
    Write-Output $body
    return
}

& gh release view $Tag *> $null
if ($LASTEXITCODE -eq 0)
{
    $body = Get-GeneratedReleaseBody $previousTag
    & gh release edit $Tag --notes $body
    if ($LASTEXITCODE -ne 0)
    {
        throw "Updating GitHub release '$Tag' failed."
    }

    & gh release upload $Tag @assets --clobber
    if ($LASTEXITCODE -ne 0)
    {
        throw "Uploading assets to GitHub release '$Tag' failed."
    }

    return
}

if (-not [string]::IsNullOrWhiteSpace($Headline))
{
    & gh release create $Tag --verify-tag --generate-notes --notes-start-tag $previousTag `
        --notes ($Headline.Trim()) @assets
}
else
{
    & gh release create $Tag --verify-tag --generate-notes --notes-start-tag $previousTag @assets
}
if ($LASTEXITCODE -ne 0)
{
    throw "Creating GitHub release '$Tag' failed."
}
