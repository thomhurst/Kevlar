[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$sourceRoot = Join-Path $RepositoryRoot 'src'
$packageIds = foreach ($projectFile in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.csproj')
{
    [xml]$project = Get-Content -LiteralPath $projectFile.FullName -Raw
    $isPackable = $project.SelectNodes('/Project/PropertyGroup/IsPackable') | Select-Object -Last 1
    if ($null -eq $isPackable -or $isPackable.InnerText -ne 'true')
    {
        continue
    }

    $packageId = $project.SelectNodes('/Project/PropertyGroup/PackageId') | Select-Object -Last 1
    if ($null -ne $packageId -and -not [string]::IsNullOrWhiteSpace($packageId.InnerText))
    {
        $packageId.InnerText
        continue
    }

    $assemblyName = $project.SelectNodes('/Project/PropertyGroup/AssemblyName') | Select-Object -Last 1
    if ($null -ne $assemblyName -and -not [string]::IsNullOrWhiteSpace($assemblyName.InnerText))
    {
        $assemblyName.InnerText
        continue
    }

    $projectFile.BaseName
}

$packageIds | Sort-Object -Unique
