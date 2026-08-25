[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$SkipMetadata
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$docsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'docs'))
$metadataPath = [IO.Path]::GetFullPath((Join-Path $docsRoot 'api'))
$outputPath = [IO.Path]::GetFullPath((Join-Path $docsRoot 'static/api'))
$configPath = Join-Path $docsRoot 'docfx.json'

foreach ($target in @($metadataPath, $outputPath))
{
    if (-not $target.StartsWith($docsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to clean generated path outside docs: $target"
    }
}

if (-not $SkipMetadata)
{
    if (-not $SkipBuild)
    {
        & dotnet build (Join-Path $repositoryRoot 'Kevlar.slnx') -c Release
        if ($LASTEXITCODE -ne 0)
        {
            throw 'Release build for DocFX metadata failed.'
        }
    }

    Remove-Item -LiteralPath $metadataPath -Recurse -Force -ErrorAction SilentlyContinue
    & dotnet docfx metadata $configPath --warningsAsErrors --disableGitFeatures
    if ($LASTEXITCODE -ne 0)
    {
        throw 'DocFX metadata generation failed.'
    }

    & (Join-Path $PSScriptRoot 'Normalize-ApiMetadata.ps1') -MetadataPath $metadataPath
}

Remove-Item -LiteralPath $outputPath -Recurse -Force -ErrorAction SilentlyContinue
& dotnet docfx build $configPath --warningsAsErrors
if ($LASTEXITCODE -ne 0)
{
    throw 'DocFX API build failed.'
}

Write-Host "Generated API metadata at '$metadataPath' and site at '$outputPath'."
