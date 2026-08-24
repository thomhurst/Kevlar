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

$unreleasedIndex = [Array]::IndexOf($lines, '## [Unreleased]')
if ($unreleasedIndex -lt 0)
{
    $errors.Add('Missing ## [Unreleased] heading.')
}

$releasePattern = [regex]'^## \[(?<version>[0-9]+\.[0-9]+\.[0-9]+)\] - (?<date>[0-9]{4}-[0-9]{2}-[0-9]{2})$'
$releases = [System.Collections.Generic.List[object]]::new()
for ($index = 0; $index -lt $lines.Count; $index++)
{
    $match = $releasePattern.Match($lines[$index])
    if (-not $match.Success)
    {
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

$tags = @(& git -C $repositoryRoot tag --list 'v*')
if ($LASTEXITCODE -ne 0)
{
    throw 'Unable to read git tags.'
}

for ($index = 0; $index -lt $releases.Count; $index++)
{
    $release = $releases[$index]
    if ($index -gt 0 -and $tags -notcontains "v$($release.Version)")
    {
        $errors.Add("Release $($release.Version) has no matching v$($release.Version) tag.")
    }

    $linkPattern = "^\[$([regex]::Escape($release.Version))\]: https://github\.com/thomhurst/Kevlar/compare/\S+$"
    if (-not ($lines | Where-Object { $_ -match $linkPattern }))
    {
        $errors.Add("Release $($release.Version) has no compare-link footer.")
    }

    if ([string]::IsNullOrWhiteSpace((Get-ReleaseNotes $release $lines)))
    {
        $errors.Add("Release $($release.Version) has no notes.")
    }
}

if (-not ($lines | Where-Object { $_ -match '^\[Unreleased\]: https://github\.com/thomhurst/Kevlar/compare/\S+$' }))
{
    $errors.Add('Unreleased has no compare-link footer.')
}

$allowedSections = @('Added', 'Changed', 'Deprecated', 'Removed', 'Fixed', 'Security')
foreach ($line in $lines | Where-Object { $_ -match '^### ' })
{
    $section = $line.Substring(4)
    if ($allowedSections -notcontains $section)
    {
        $errors.Add("Unsupported changelog section '$section'.")
    }
}

$deadApiPattern = [regex]'FallbackWithNotifications|\bWhenDefault\(|\bOrDefault\(|\bOrWhen\(|WhenResultDefault|OrResultDefault|\bKEV005\b|\bHedgingOptions\b'
$documents = @($changelogPath) + @(
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs') -Recurse -File -Include '*.md', '*.mdx' |
        ForEach-Object FullName)
foreach ($document in $documents)
{
    $insideUpgradeTable = $false
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $document)
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
            $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $document).Replace('\', '/')
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
