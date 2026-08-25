[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version,

    [string]$ChangelogPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md'),

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$lines = @(Get-Content -LiteralPath $ChangelogPath)
$start = -1

foreach ($heading in @("## [$Version]", '## [Unreleased]', '## Unreleased'))
{
    for ($index = 0; $index -lt $lines.Count; $index++)
    {
        if ($lines[$index] -eq $heading -or $lines[$index].StartsWith("$heading - ", [StringComparison]::Ordinal))
        {
            $start = $index + 1
            break
        }
    }

    if ($start -ge 0)
    {
        break
    }
}

if ($start -lt 0)
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
