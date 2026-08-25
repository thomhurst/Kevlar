[CmdletBinding()]
param(
    [string]$ReleaseVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$changelogPath = Join-Path $repositoryRoot 'CHANGELOG.md'
$lines = @(Get-Content -LiteralPath $changelogPath)
$errors = [System.Collections.Generic.List[string]]::new()
$allowedSections = @('Added', 'Changed', 'Deprecated', 'Removed', 'Fixed', 'Security')

function Get-ReleaseNotes([object]$Release, [string[]]$ChangelogLines)
{
    $end = $ChangelogLines.Count
    for ($index = $Release.Index + 1; $index -lt $ChangelogLines.Count; $index++)
    {
        if ($ChangelogLines[$index] -match '^## ' -or $ChangelogLines[$index] -match '^\[(?:Unreleased|[0-9])')
        {
            $end = $index
            break
        }
    }

    if ($end -le $Release.Index + 1)
    {
        return ''
    }

    ($ChangelogLines[($Release.Index + 1)..($end - 1)] -join [Environment]::NewLine).Trim()
}

function Test-ReleaseHasEntry([object]$Release, [string[]]$ChangelogLines)
{
    $insideAllowedSection = $false
    for ($index = $Release.Index + 1; $index -lt $ChangelogLines.Count; $index++)
    {
        $line = $ChangelogLines[$index]
        if ($line -match '^## ' -or $line -match '^\[(?:Unreleased|[0-9])')
        {
            break
        }

        if ($line -match '^### (?<section>.+)$')
        {
            $insideAllowedSection = $allowedSections -contains $Matches['section']
            continue
        }

        if ($insideAllowedSection -and $line -match '^\s*[-*]\s+\S')
        {
            return $true
        }
    }

    return $false
}

$unreleasedHeadingCount = @($lines | Where-Object { $_ -eq '## [Unreleased]' }).Count
$unreleasedIndex = [Array]::IndexOf($lines, '## [Unreleased]')
if ($unreleasedHeadingCount -ne 1)
{
    $errors.Add("CHANGELOG.md must contain exactly one ## [Unreleased] heading; found $unreleasedHeadingCount.")
}

$versionComponentPattern = '(?:0|[1-9][0-9]*)'
$releasePattern = [regex]"^## \[(?<version>$versionComponentPattern\.$versionComponentPattern\.$versionComponentPattern)\] - (?<date>[0-9]{4}-[0-9]{2}-[0-9]{2})$"
$releases = [System.Collections.Generic.List[object]]::new()
for ($index = 0; $index -lt $lines.Count; $index++)
{
    $match = $releasePattern.Match($lines[$index])
    if (-not $match.Success)
    {
        if ($lines[$index] -match '^##(?:\s|$)' -and $lines[$index] -ne '## [Unreleased]')
        {
            $errors.Add("Malformed versioned release heading '$($lines[$index])'. Expected '## [x.y.z] - yyyy-MM-dd'.")
        }

        continue
    }

    $date = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact(
        $match.Groups['date'].Value,
        'yyyy-MM-dd',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::None,
        [ref]$date))
    {
        $errors.Add("Invalid release date in '$($lines[$index])'.")
    }

    $releases.Add([pscustomobject]@{
        Version = $match.Groups['version'].Value
        Index = $index
    })
}

if ($releases.Count -eq 0)
{
    $errors.Add('No versioned release headings found.')
}
elseif ($unreleasedIndex -gt $releases[0].Index)
{
    $errors.Add('## [Unreleased] must precede every versioned release.')
}

for ($index = 1; $index -lt $releases.Count; $index++)
{
    $newerVersion = [Version]$releases[$index - 1].Version
    $olderVersion = [Version]$releases[$index].Version
    if ($newerVersion -le $olderVersion)
    {
        $errors.Add("Release versions must be strictly descending; $newerVersion appears before $olderVersion.")
    }
}

$tags = @(& git -C $repositoryRoot tag --list 'v*')
if ($LASTEXITCODE -ne 0)
{
    throw 'Unable to read git tags.'
}

$stableTags = @($tags | ForEach-Object {
    if ($_ -match '^v(?<version>[0-9]+\.[0-9]+\.[0-9]+)$')
    {
        [pscustomobject]@{
            Name = $_
            Version = [Version]$Matches['version']
        }
    }
})

for ($index = 0; $index -lt $releases.Count; $index++)
{
    $release = $releases[$index]
    if ($index -gt 0 -and $tags -notcontains "v$($release.Version)")
    {
        $errors.Add("Release $($release.Version) has no matching v$($release.Version) tag.")
    }

    $previousTag = if ($index + 1 -lt $releases.Count)
    {
        "v$($releases[$index + 1].Version)"
    }
    else
    {
        $currentVersion = [Version]$release.Version
        $previousStableTag = $stableTags |
            Where-Object Version -lt $currentVersion |
            Sort-Object Version -Descending |
            Select-Object -First 1
        if ($null -eq $previousStableTag) { $null } else { $previousStableTag.Name }
    }

    if ([string]::IsNullOrWhiteSpace($previousTag))
    {
        $errors.Add("Release $($release.Version) has no earlier stable tag for its compare link.")
    }
    else
    {
        $expectedLink = "[$($release.Version)]: https://github.com/thomhurst/Kevlar/compare/$previousTag...v$($release.Version)"
        $actualLinks = @($lines | Where-Object { $_.StartsWith("[$($release.Version)]:", [StringComparison]::Ordinal) })
        if ($actualLinks.Count -ne 1 -or $actualLinks[0] -ne $expectedLink)
        {
            $errors.Add("Release $($release.Version) compare link must be '$expectedLink'.")
        }
    }

    if (-not (Test-ReleaseHasEntry $release $lines))
    {
        $errors.Add("Release $($release.Version) has no entry under an allowed section.")
    }
}

if ($releases.Count -gt 0)
{
    $expectedUnreleasedLink = "[Unreleased]: https://github.com/thomhurst/Kevlar/compare/v$($releases[0].Version)...HEAD"
    $actualUnreleasedLinks = @($lines | Where-Object { $_.StartsWith('[Unreleased]:', [StringComparison]::Ordinal) })
    if ($actualUnreleasedLinks.Count -ne 1 -or $actualUnreleasedLinks[0] -ne $expectedUnreleasedLink)
    {
        $errors.Add("Unreleased compare link must be '$expectedUnreleasedLink'.")
    }
}

foreach ($line in $lines | Where-Object { $_ -match '^### ' })
{
    $section = $line.Substring(4)
    if ($allowedSections -notcontains $section)
    {
        $errors.Add("Unsupported changelog section '$section'.")
    }
}

$deadApiPattern = [regex]'FallbackWithNotifications|\bWhenDefault\(|\bOrDefault\(|\bOrWhen\(|WhenResultDefault|OrResultDefault|\bHedgingOptions\b|\bHedgingStrategyDescriptor\b|\bStrategyKind\.Hedging\b|\bStandardHedgingShieldOptions\b|\bAddStandardHedgingShield\b|\bVoidShield\b|\bVoidShieldBuilder\b|\bPartitionedVoidShield\b|\bmaxQueue\b|\bMaxQueue\b'
$documents = @($changelogPath, (Join-Path $repositoryRoot 'README.md')) + @(
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs') -Recurse -File -Include '*.md', '*.mdx' |
        ForEach-Object FullName)
foreach ($document in $documents)
{
    $documentLines = @(Get-Content -LiteralPath $document)
    $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $document).Replace('\', '/')
    $startMarkers = @($documentLines | Select-String -Pattern '^<!-- upgrade-from-0\.x:start -->$')
    $endMarkers = @($documentLines | Select-String -Pattern '^<!-- upgrade-from-0\.x:end -->$')
    if ($startMarkers.Count -ne $endMarkers.Count -or $startMarkers.Count -gt 1)
    {
        $errors.Add("$relativePath has unbalanced or duplicate upgrade-table markers: $($startMarkers.Count) start and $($endMarkers.Count) end markers.")
    }
    elseif ($startMarkers.Count -eq 1 -and $startMarkers[0].LineNumber -ge $endMarkers[0].LineNumber)
    {
        $errors.Add("$relativePath upgrade-table markers are out of order.")
    }
    elseif ($document -eq $changelogPath -and $startMarkers.Count -eq 0)
    {
        $errors.Add('CHANGELOG.md is missing its upgrade-table marker pair.')
    }

    $insideUpgradeTable = $false
    $lineNumber = 0
    foreach ($line in $documentLines)
    {
        $lineNumber++
        if ($line -eq '<!-- upgrade-from-0.x:start -->')
        {
            $insideUpgradeTable = $true
            continue
        }
        if ($line -eq '<!-- upgrade-from-0.x:end -->')
        {
            $insideUpgradeTable = $false
            continue
        }
        if (-not $insideUpgradeTable -and $deadApiPattern.IsMatch($line))
        {
            $errors.Add("$relativePath`:$lineNumber contains a dead pre-release API name.")
        }
    }
}

if ($errors.Count -gt 0)
{
    throw "Changelog verification failed:`n- $($errors -join "`n- ")"
}

if ($ReleaseVersion)
{
    $release = $releases | Where-Object Version -eq $ReleaseVersion | Select-Object -First 1
    if ($null -eq $release)
    {
        throw "Release $ReleaseVersion was not found in CHANGELOG.md."
    }

    $notes = Get-ReleaseNotes $release $lines
    if ([string]::IsNullOrWhiteSpace($notes))
    {
        throw "Release $ReleaseVersion has no notes."
    }

    $notes
    exit 0
}

Write-Host "Verified $($releases.Count) changelog release(s) and migration API names."
