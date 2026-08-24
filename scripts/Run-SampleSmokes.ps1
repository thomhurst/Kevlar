[CmdletBinding()]
param(
    [string]$SamplesPath,
    [switch]$UsePackedPackages,
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
if (-not $SamplesPath)
{
    $SamplesPath = Join-Path $repositoryRoot 'samples'
}

$resolvedSamplesPath = (Resolve-Path -LiteralPath $SamplesPath).Path
$projects = @(Get-ChildItem -LiteralPath $resolvedSamplesPath -Directory |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.csproj' -File } |
    Sort-Object FullName)
$frameworks = @('net8.0', 'net10.0')
$properties = if ($UsePackedPackages)
{
    @('-p:UsePackedPackages=true', "-p:KevlarSamplePackageVersion=$Version")
}
else
{
    @()
}

foreach ($project in $projects)
{
    foreach ($framework in $frameworks)
    {
        & dotnet run --project $project.FullName -c Release -f $framework --no-build @properties -- --smoke
        if ($LASTEXITCODE -ne 0)
        {
            throw "Sample '$($project.BaseName)' failed for $framework."
        }
    }
}

Write-Host "All $($projects.Count) samples passed on $($frameworks -join ' and ')."
