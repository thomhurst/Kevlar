[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagesPath,

    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$resolvedPackagesPath = (Resolve-Path -LiteralPath $PackagesPath).Path
$sdkVersion = dotnet --list-sdks |
    ForEach-Object { ($_ -split '\s+', 2)[0] } |
    Where-Object { $_ -match '^7\.' } |
    Sort-Object { [Version]$_ } -Descending |
    Select-Object -First 1
if (-not $sdkVersion)
{
    throw 'A .NET 7 SDK is required for the pre-Roslyn-4.8 analyzer compatibility check.'
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "kevlar-analyzer-compat-$([Guid]::NewGuid().ToString('N'))"
$utf8 = [Text.UTF8Encoding]::new($false)
$previousNuGetPackages = $env:NUGET_PACKAGES

try
{
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $env:NUGET_PACKAGES = Join-Path $testRoot 'packages'
    [IO.File]::WriteAllText(
        (Join-Path $testRoot 'global.json'),
        (@{
            sdk = @{
                version = $sdkVersion
                rollForward = 'disable'
            }
        } | ConvertTo-Json -Depth 2),
        $utf8)

    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net7.0</TargetFramework>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <SuppressTfmSupportBuildWarnings>true</SuppressTfmSupportBuildWarnings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kevlar" Version="$Version" />
  </ItemGroup>
</Project>
"@
    [IO.File]::WriteAllText((Join-Path $testRoot 'AnalyzerCompatibility.csproj'), $project, $utf8)
    [IO.File]::WriteAllText(
        (Join-Path $testRoot 'Program.cs'),
        "using System;`nusing Kevlar;`n`nConsole.WriteLine(Shield.Empty);`n",
        $utf8)

    $escapedPackagesPath = [Security.SecurityElement]::Escape($resolvedPackagesPath)
    $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-packages" value="$escapedPackagesPath" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
    $nugetConfigPath = Join-Path $testRoot 'NuGet.config'
    [IO.File]::WriteAllText($nugetConfigPath, $nugetConfig, $utf8)

    Push-Location $testRoot
    try
    {
        $selectedSdk = dotnet --version
        if ($selectedSdk -ne $sdkVersion)
        {
            throw "Expected .NET SDK $sdkVersion, got $selectedSdk."
        }

        dotnet restore AnalyzerCompatibility.csproj --configfile $nugetConfigPath
        if ($LASTEXITCODE -ne 0)
        {
            throw "Analyzer compatibility restore failed with exit code $LASTEXITCODE."
        }

        $buildOutput = @(dotnet build AnalyzerCompatibility.csproj -c Release --no-restore -warnaserror 2>&1)
        $buildOutput | Write-Host
        if ($buildOutput -match 'CS9057')
        {
            throw 'The pre-Roslyn-4.8 compiler attempted to load the Kevlar analyzer (CS9057).'
        }
        if ($LASTEXITCODE -ne 0)
        {
            throw "Analyzer compatibility build failed with exit code $LASTEXITCODE."
        }
    }
    finally
    {
        Pop-Location
    }
}
finally
{
    if ($null -eq $previousNuGetPackages)
    {
        Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
    }
    else
    {
        $env:NUGET_PACKAGES = $previousNuGetPackages
    }

    if (Test-Path -LiteralPath $testRoot)
    {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
