[CmdletBinding()]
param(
    [string]$SamplesPath,
    [string]$ReadmePath
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
if (-not $SamplesPath)
{
    $SamplesPath = Join-Path $repositoryRoot 'samples'
}

if (-not $ReadmePath)
{
    $ReadmePath = Join-Path $repositoryRoot 'README.md'
}

$resolvedSamplesPath = (Resolve-Path -LiteralPath $SamplesPath).Path
$resolvedReadmePath = (Resolve-Path -LiteralPath $ReadmePath).Path
$readme = Get-Content -LiteralPath $resolvedReadmePath -Raw
$sampleDirectories = @(Get-ChildItem -LiteralPath $resolvedSamplesPath -Directory | Sort-Object Name)
$errors = [System.Collections.Generic.List[string]]::new()

if ($sampleDirectories.Count -eq 0)
{
    $errors.Add("No sample directories were found under '$resolvedSamplesPath'.")
}

foreach ($sampleDirectory in $sampleDirectories)
{
    $projectFiles = @(Get-ChildItem -LiteralPath $sampleDirectory.FullName -Filter '*.csproj' -File)
    if ($projectFiles.Count -ne 1)
    {
        $errors.Add("Sample '$($sampleDirectory.Name)' must contain exactly one project file.")
    }

    if (-not (Test-Path -LiteralPath (Join-Path $sampleDirectory.FullName 'README.md') -PathType Leaf))
    {
        $errors.Add("Sample '$($sampleDirectory.Name)' has no README.md.")
    }

    if ($readme -notmatch [regex]::Escape("samples/$($sampleDirectory.Name)"))
    {
        $errors.Add("README.md does not list samples/$($sampleDirectory.Name).")
    }
}

if ($errors.Count -gt 0)
{
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

Write-Host "Verified $($sampleDirectories.Count) runnable sample projects and README entries."
