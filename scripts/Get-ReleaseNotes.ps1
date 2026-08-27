[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$Version,

    [string]$ChangelogPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md'),

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$lines = @(Get-Content -LiteralPath $ChangelogPath)
$releaseVersion = $Version.Split('+', 2)[0].Split('-', 2)[0]
$releaseHeading = "## [$releaseVersion]"
$releaseIndex = -1
$unreleasedIndex = -1

for ($index = 0; $index -lt $lines.Count; $index++)
{
    if ($releaseIndex -lt 0 -and
        ($lines[$index] -eq $releaseHeading -or
         $lines[$index].StartsWith("$releaseHeading - ", [StringComparison]::Ordinal)))
    {
        $releaseIndex = $index
    }

    if ($unreleasedIndex -lt 0 -and $lines[$index] -in '## [Unreleased]', '## Unreleased')
    {
        $unreleasedIndex = $index
    }
}

if ($releaseIndex -ge 0 -and $unreleasedIndex -ge 0)
{
    $unreleasedEnd = $lines.Count
    for ($index = $unreleasedIndex + 1; $index -lt $lines.Count; $index++)
    {
        if ($lines[$index] -match '^## ')
        {
            $unreleasedEnd = $index
            break
        }
    }

    $unreleasedNotes = if ($unreleasedEnd -gt $unreleasedIndex + 1)
    {
        ($lines[($unreleasedIndex + 1)..($unreleasedEnd - 1)] -join [Environment]::NewLine).Trim()
    }
    else
    {
        ''
    }

    if (-not [string]::IsNullOrWhiteSpace($unreleasedNotes))
    {
        throw "Release $releaseVersion exists beside a populated Unreleased section in '$ChangelogPath'."
    }
}

$start = if ($releaseIndex -ge 0)
{
    $releaseIndex + 1
}
elseif ($unreleasedIndex -ge 0)
{
    $unreleasedIndex + 1
}
else
{
    throw "Release $Version and the Unreleased section were not found in '$ChangelogPath'."
}

$end = $lines.Count
for ($index = $start; $index -lt $lines.Count; $index++)
{
    if ($lines[$index] -match '^## ' -or $lines[$index] -match '^\[(?:Unreleased|[0-9])')
    {
        $end = $index
        break
    }
}

$notes = if ($end -gt $start)
{
    ($lines[$start..($end - 1)] -join [Environment]::NewLine).Trim()
}
else
{
    ''
}

if ([string]::IsNullOrWhiteSpace($notes))
{
    throw "Release $Version has no notes in '$ChangelogPath'."
}

if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $notes
    exit 0
}

$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [IO.Path]::GetDirectoryName($resolvedOutputPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory))
{
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

[IO.File]::WriteAllText(
    $resolvedOutputPath,
    "$notes$([Environment]::NewLine)",
    [Text.UTF8Encoding]::new($false))
