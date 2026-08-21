[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagesPath,

    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-Equal([string]$Name, [AllowNull()]$Actual, [AllowNull()]$Expected)
{
    if ($Actual -ne $Expected)
    {
        throw "${Name}: expected '$Expected', got '$Actual'."
    }
}

function Assert-Set([string]$Name, [object[]]$Actual, [object[]]$Expected)
{
    $actualText = (@($Actual) | Sort-Object -Unique) -join ', '
    $expectedText = (@($Expected) | Sort-Object -Unique) -join ', '
    Assert-Equal $Name $actualText $expectedText
}

function Get-NodeText([System.Xml.XmlNode]$Node, [string]$XPath)
{
    $match = $Node.SelectSingleNode($XPath)
    if ($null -eq $match)
    {
        return $null
    }

    if ($match -is [System.Xml.XmlAttribute])
    {
        return $match.Value
    }

    return $match.InnerText
}

function Write-TextFile([string]$Path, [string]$Contents)
{
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Path)) | Out-Null
    [System.IO.File]::WriteAllText($Path, $Contents, [System.Text.UTF8Encoding]::new($false))
}

function Invoke-DotNet([string[]]$Arguments)
{
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$buildPropertiesJson = & dotnet msbuild `
    (Join-Path $repositoryRoot 'src/Kevlar/Kevlar.csproj') `
    -nologo `
    -p:CI=true `
    -getProperty:Deterministic,ContinuousIntegrationBuild,DeterministicSourcePaths,PublishRepositoryUrl,EmbedUntrackedSources
if ($LASTEXITCODE -ne 0)
{
    throw 'Unable to inspect deterministic build properties.'
}

$buildProperties = ($buildPropertiesJson | Out-String | ConvertFrom-Json).Properties
Assert-Equal 'Deterministic' $buildProperties.Deterministic 'true'
Assert-Equal 'ContinuousIntegrationBuild' $buildProperties.ContinuousIntegrationBuild 'true'
Assert-Equal 'DeterministicSourcePaths' $buildProperties.DeterministicSourcePaths 'true'
Assert-Equal 'PublishRepositoryUrl' $buildProperties.PublishRepositoryUrl 'true'
Assert-Equal 'EmbedUntrackedSources' $buildProperties.EmbedUntrackedSources 'true'

$packageDirectory = (Resolve-Path -LiteralPath $PackagesPath).Path
$expectedRepositoryCommit = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($expectedRepositoryCommit))
{
    throw 'Unable to determine the expected repository commit.'
}

$expectedDependencies = @{
    'Kevlar' = @{
        'net10.0' = @('Reservoir')
        '.NETStandard2.0' = @('Microsoft.Bcl.TimeProvider', 'Reservoir', 'System.Threading.Tasks.Extensions')
    }
    'Kevlar.Extensions.DependencyInjection' = @{
        'net10.0' = @('Kevlar', 'Microsoft.Extensions.Configuration.Binder', 'Microsoft.Extensions.DependencyInjection.Abstractions')
        '.NETStandard2.0' = @('Kevlar', 'Microsoft.Extensions.Configuration.Binder', 'Microsoft.Extensions.DependencyInjection.Abstractions')
    }
    'Kevlar.Extensions.Http' = @{
        'net10.0' = @('Kevlar', 'Microsoft.Extensions.Http')
        '.NETStandard2.0' = @('Kevlar', 'Microsoft.Extensions.Http')
    }
    'Kevlar.Analyzers' = @{
        '.NETStandard2.0' = @()
    }
}

$packageFiles = @(Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nupkg' -File)
Assert-Set 'package IDs' ($packageFiles.BaseName | ForEach-Object { $_ -replace "\.$([regex]::Escape($Version))$", '' }) @($expectedDependencies.Keys | ForEach-Object { $_ })

foreach ($packageId in $expectedDependencies.Keys)
{
    $packageFile = Join-Path $packageDirectory "$packageId.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $packageFile -PathType Leaf))
    {
        throw "Missing package '$packageFile'."
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($packageFile)
    try
    {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        $nuspecEntry = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
        Assert-Equal "$packageId nuspec count" $nuspecEntry.Count 1

        $reader = [System.IO.StreamReader]::new($nuspecEntry[0].Open())
        try
        {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally
        {
            $reader.Dispose()
        }

        $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        if ($null -eq $metadata)
        {
            throw "$packageId has no nuspec metadata."
        }

        Assert-Equal "$packageId ID" (Get-NodeText $metadata "*[local-name()='id']") $packageId
        Assert-Equal "$packageId version" (Get-NodeText $metadata "*[local-name()='version']") $Version
        Assert-Equal "$packageId license" (Get-NodeText $metadata "*[local-name()='license']") 'MIT'
        Assert-Equal "$packageId README metadata" (Get-NodeText $metadata "*[local-name()='readme']") 'README.md'
        Assert-Equal "$packageId repository URL" (Get-NodeText $metadata "*[local-name()='repository']/@url") 'https://github.com/thomhurst/Kevlar'
        Assert-Equal "$packageId repository type" (Get-NodeText $metadata "*[local-name()='repository']/@type") 'git'
        Assert-Equal "$packageId repository commit" (Get-NodeText $metadata "*[local-name()='repository']/@commit") $expectedRepositoryCommit

        if ($entries -notcontains 'README.md')
        {
            throw "$packageId does not contain README.md."
        }

        $groups = @($metadata.SelectNodes("*[local-name()='dependencies']/*[local-name()='group']"))
        Assert-Set "$packageId dependency groups" ($groups | ForEach-Object { $_.GetAttribute('targetFramework') }) @($expectedDependencies[$packageId].Keys | ForEach-Object { $_ })
        foreach ($group in $groups)
        {
            $framework = $group.GetAttribute('targetFramework')
            $dependencies = @($group.SelectNodes("*[local-name()='dependency']"))
            Assert-Set "$packageId $framework dependencies" ($dependencies | ForEach-Object { $_.GetAttribute('id') }) $expectedDependencies[$packageId][$framework]

            foreach ($dependency in $dependencies)
            {
                Assert-Set "$packageId $framework $($dependency.GetAttribute('id')) excluded assets" ($dependency.GetAttribute('exclude') -split ',') @('Build', 'Analyzers')
            }
        }

        $privateDependencies = @('Polyfill', 'Microsoft.CodeAnalysis.CSharp', 'Microsoft.CodeAnalysis.PublicApiAnalyzers')
        $allDependencyIds = @($groups | ForEach-Object { $_.SelectNodes("*[local-name()='dependency']") } | ForEach-Object { $_.GetAttribute('id') })
        foreach ($privateDependency in $privateDependencies)
        {
            if ($allDependencyIds -contains $privateDependency)
            {
                throw "$packageId exposes private build dependency '$privateDependency'."
            }
        }

        if ($packageId -eq 'Kevlar.Analyzers')
        {
            $assemblyEntries = @($entries | Where-Object { $_ -like '*.dll' })
            Assert-Set "$packageId assemblies" $assemblyEntries @('analyzers/dotnet/cs/Kevlar.Analyzers.dll')
            if ($entries | Where-Object { $_ -match '^(lib|build|buildTransitive|tools)/' })
            {
                throw "$packageId contains an unexpected runtime or build asset."
            }
        }
        else
        {
            $expectedAssets = @(
                "lib/net10.0/$packageId.dll",
                "lib/net10.0/$packageId.xml",
                "lib/netstandard2.0/$packageId.dll",
                "lib/netstandard2.0/$packageId.xml"
            )
            Assert-Set "$packageId library assets" ($entries | Where-Object { $_ -like 'lib/*' }) $expectedAssets
            if ($entries | Where-Object { $_ -match '^(analyzers|build|buildTransitive|tools)/' })
            {
                throw "$packageId contains an unexpected analyzer or build asset."
            }
        }
    }
    finally
    {
        $archive.Dispose()
    }
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "kevlar-package-consumers-$([guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
$previousPackagesPath = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = Join-Path $temporaryRoot '.packages'

try
{
    $escapedPackageDirectory = [System.Security.SecurityElement]::Escape($packageDirectory)
    $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedPackageDirectory" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="Kevlar" />
      <package pattern="Kevlar.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
    $nugetConfigPath = Join-Path $temporaryRoot 'NuGet.Config'
    Write-TextFile $nugetConfigPath $nugetConfig

    $runtimeProgram = @'
using Kevlar;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

var shield = Shield.Empty;
var value = await shield.ExecuteAsync(static cancellationToken =>
    new ValueTask<int>(cancellationToken.CanBeCanceled ? 41 : 42));
if (value != 42)
{
    throw new InvalidOperationException("Core package execution failed.");
}

IServiceCollection services = new ServiceCollection();
services.AddShield("consumer", shield);
services.AddHttpClient("consumer").AddStandardShield();
Console.WriteLine("Kevlar package consumer passed.");
'@

    foreach ($framework in @('net8.0', 'net10.0'))
    {
        $consumerDirectory = Join-Path $temporaryRoot "runtime-$framework"
        $consumerProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$framework</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kevlar" Version="$Version" />
    <PackageReference Include="Kevlar.Extensions.DependencyInjection" Version="$Version" />
    <PackageReference Include="Kevlar.Extensions.Http" Version="$Version" />
  </ItemGroup>
</Project>
"@
        $projectPath = Join-Path $consumerDirectory 'Consumer.csproj'
        Write-TextFile $projectPath $consumerProject
        Write-TextFile (Join-Path $consumerDirectory 'Program.cs') $runtimeProgram
        Invoke-DotNet @('restore', $projectPath, '--configfile', $nugetConfigPath, '--no-cache', '--force-evaluate')
        Invoke-DotNet @('run', '--project', $projectPath, '-c', 'Release', '--no-restore')
    }

    $analyzerDirectory = Join-Path $temporaryRoot 'analyzer'
    $analyzerProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kevlar" Version="$Version" />
    <PackageReference Include="Kevlar.Analyzers" Version="$Version" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@
    Write-TextFile (Join-Path $analyzerDirectory 'AnalyzerConsumer.csproj') $analyzerProject
    Write-TextFile (Join-Path $analyzerDirectory 'Program.cs') @'
using Kevlar;

await Shield.Empty.ExecuteAsync(cancellationToken => ValueTask.CompletedTask);
'@
    $analyzerProjectPath = Join-Path $analyzerDirectory 'AnalyzerConsumer.csproj'
    Invoke-DotNet @('restore', $analyzerProjectPath, '--configfile', $nugetConfigPath, '--no-cache', '--force-evaluate')
    $analyzerOutput = (& dotnet build $analyzerProjectPath -c Release --no-restore -p:TreatWarningsAsErrors=false -warnaserror:KEV001 2>&1 | Out-String)
    $analyzerExitCode = $LASTEXITCODE
    $analyzerErrorLines = @(
        $analyzerOutput -split '\r?\n' |
            Where-Object { $_ -match '(?i)\berror(?:\s+[A-Z]+\d+)?\s*:' }
    )
    $analyzerErrorCodes = @(
        [regex]::Matches($analyzerOutput, '(?m)\berror\s+([A-Z]+\d+)\s*:') |
            ForEach-Object { $_.Groups[1].Value }
    )
    if ($analyzerExitCode -eq 0)
    {
        throw "Kevlar.Analyzers package did not fail the consumer build with KEV001.`n$analyzerOutput"
    }

    Assert-Set 'analyzer consumer error codes' $analyzerErrorCodes @('KEV001')
    $unexpectedAnalyzerErrors = @(
        $analyzerErrorLines |
            Where-Object { $_ -notmatch '(?i)\berror\s+KEV001\s*:' }
    )
    if ($unexpectedAnalyzerErrors.Count -gt 0)
    {
        throw "Analyzer consumer produced errors other than KEV001:`n$($unexpectedAnalyzerErrors -join [Environment]::NewLine)"
    }

    Write-Host 'All package layout, metadata, consumer, and analyzer checks passed.'
}
finally
{
    $env:NUGET_PACKAGES = $previousPackagesPath
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
}

# The analyzer check intentionally runs a failing dotnet build. Do not leak that
# expected native exit code after every assertion and cleanup step has passed.
exit 0
