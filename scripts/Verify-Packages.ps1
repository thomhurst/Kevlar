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

function Get-ExpectedSymbolAssets([string]$PackageId)
{
    if ($PackageId -eq 'Kevlar.Analyzers')
    {
        return @('analyzers/dotnet/cs/Kevlar.Analyzers.pdb')
    }

    $frameworks = @('net10.0', 'netstandard2.0')
    if ($PackageId -in @('Kevlar.Chaos', 'Kevlar.Testing'))
    {
        $frameworks += 'net8.0'
    }
    elseif ($PackageId -eq 'Kevlar.Extensions.Grpc')
    {
        $frameworks += 'netstandard2.1'
    }

    return @($frameworks | ForEach-Object { "lib/$_/$PackageId.pdb" })
}

function Assert-DeterministicAssembly(
    [string]$Name,
    [System.IO.Compression.ZipArchiveEntry]$Entry)
{
    $stream = $Entry.Open()
    $buffer = [System.IO.MemoryStream]::new()
    try
    {
        $stream.CopyTo($buffer)
        $buffer.Position = 0
        $reader = [System.Reflection.PortableExecutable.PEReader]::new($buffer)
        try
        {
            $debugEntryTypes = @($reader.ReadDebugDirectory() | ForEach-Object Type)
            if ($debugEntryTypes -notcontains [System.Reflection.PortableExecutable.DebugDirectoryEntryType]::Reproducible)
            {
                throw "$Name is missing the deterministic PE marker."
            }
        }
        finally
        {
            $reader.Dispose()
        }
    }
    finally
    {
        $buffer.Dispose()
        $stream.Dispose()
    }
}

function Get-ArchiveEntryHash([string]$ArchivePath, [string]$EntryPath)
{
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try
    {
        $entry = $archive.GetEntry($EntryPath)
        if ($null -eq $entry)
        {
            throw "Archive '$ArchivePath' does not contain '$EntryPath'."
        }

        $stream = $entry.Open()
        $hash = [System.Security.Cryptography.SHA256]::Create()
        try
        {
            return [Convert]::ToHexString($hash.ComputeHash($stream))
        }
        finally
        {
            $hash.Dispose()
            $stream.Dispose()
        }
    }
    finally
    {
        $archive.Dispose()
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$buildPropertiesJson = & dotnet msbuild `
    (Join-Path $repositoryRoot 'src/Kevlar/Kevlar.csproj') `
    -nologo `
    -p:CI=true `
    -getProperty:Deterministic,ContinuousIntegrationBuild,DeterministicSourcePaths,PublishRepositoryUrl,EmbedUntrackedSources,IncludeSymbols,SymbolPackageFormat
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
Assert-Equal 'IncludeSymbols' $buildProperties.IncludeSymbols 'true'
Assert-Equal 'SymbolPackageFormat' $buildProperties.SymbolPackageFormat 'snupkg'

$packageDirectory = (Resolve-Path -LiteralPath $PackagesPath).Path
$expectedRepositoryCommit = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($expectedRepositoryCommit))
{
    throw 'Unable to determine the expected repository commit.'
}

$centralPackagesPath = Join-Path $PSScriptRoot '..\Directory.Packages.props'
[xml]$centralPackages = Get-Content -LiteralPath $centralPackagesPath -Raw
$configurationVersion = Get-NodeText $centralPackages "/Project/ItemGroup/PackageVersion[@Include='Microsoft.Extensions.Configuration']/@Version"
$dependencyInjectionVersion = Get-NodeText $centralPackages "/Project/ItemGroup/PackageVersion[@Include='Microsoft.Extensions.DependencyInjection']/@Version"
if ($null -eq $configurationVersion -or $null -eq $dependencyInjectionVersion)
{
    throw 'Could not resolve Microsoft.Extensions package versions from Directory.Packages.props.'
}

$expectedDependencies = @{
    'Kevlar' = @{
        'net10.0' = @('Reservoir')
        '.NETStandard2.0' = @('Microsoft.Bcl.TimeProvider', 'Reservoir', 'System.Threading.Tasks.Extensions')
    }
    'Kevlar.Extensions.DependencyInjection' = @{
        'net10.0' = @('Kevlar', 'Microsoft.Extensions.Configuration.Abstractions', 'Microsoft.Extensions.DependencyInjection.Abstractions', 'Microsoft.Extensions.Primitives')
        '.NETStandard2.0' = @('Kevlar', 'Microsoft.Extensions.Configuration.Abstractions', 'Microsoft.Extensions.DependencyInjection.Abstractions', 'Microsoft.Extensions.Primitives')
    }
    'Kevlar.Extensions.Http' = @{
        'net10.0' = @('Kevlar', 'Microsoft.Extensions.Configuration.Abstractions', 'Microsoft.Extensions.Http', 'Microsoft.Extensions.Primitives')
        '.NETStandard2.0' = @('Kevlar', 'Microsoft.Extensions.Configuration.Abstractions', 'Microsoft.Extensions.Http', 'Microsoft.Extensions.Primitives')
    }
    'Kevlar.Extensions.RateLimiting' = @{
        'net10.0' = @('Kevlar', 'System.Threading.RateLimiting')
        '.NETStandard2.0' = @('Kevlar', 'System.Threading.RateLimiting')
    }
    'Kevlar.Chaos' = @{
        'net10.0' = @('Kevlar')
        'net8.0' = @('Kevlar')
        '.NETStandard2.0' = @(
            'Kevlar',
            'Microsoft.Bcl.TimeProvider',
            'System.Runtime.CompilerServices.Unsafe',
            'System.Threading.Tasks.Extensions')
    }
    'Kevlar.Testing' = @{
        'net10.0' = @('Kevlar', 'Microsoft.Extensions.TimeProvider.Testing')
        'net8.0' = @('Kevlar', 'Microsoft.Extensions.TimeProvider.Testing')
        '.NETStandard2.0' = @('Kevlar')
    }
    'Kevlar.Extensions.Grpc' = @{
        'net10.0' = @('Grpc.Core.Api', 'Grpc.Net.ClientFactory', 'Kevlar', 'Kevlar.Extensions.DependencyInjection')
        '.NETStandard2.1' = @('Grpc.Core.Api', 'Grpc.Net.ClientFactory', 'Kevlar', 'Kevlar.Extensions.DependencyInjection')
        '.NETStandard2.0' = @('Grpc.Core.Api', 'Grpc.Net.ClientFactory', 'Kevlar', 'Kevlar.Extensions.DependencyInjection')
    }
    'Kevlar.Analyzers' = @{
        '.NETStandard2.0' = @()
    }
}

$packageFiles = @(Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nupkg' -File)
Assert-Set 'package IDs' ($packageFiles.BaseName | ForEach-Object { $_ -replace "\.$([regex]::Escape($Version))$", '' }) @($expectedDependencies.Keys | ForEach-Object { $_ })
$symbolPackageFiles = @(Get-ChildItem -LiteralPath $packageDirectory -Filter '*.snupkg' -File)
Assert-Set 'symbol package IDs' ($symbolPackageFiles.BaseName | ForEach-Object { $_ -replace "\.$([regex]::Escape($Version))$", '' }) @($expectedDependencies.Keys | ForEach-Object { $_ })

foreach ($packageId in $expectedDependencies.Keys)
{
    $packageFile = Join-Path $packageDirectory "$packageId.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $packageFile -PathType Leaf))
    {
        throw "Missing package '$packageFile'."
    }

    $symbolPackageFile = Join-Path $packageDirectory "$packageId.$Version.snupkg"
    if (-not (Test-Path -LiteralPath $symbolPackageFile -PathType Leaf))
    {
        throw "Missing symbol package '$symbolPackageFile'."
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
            Assert-Set "$packageId analyzer assets" `
                ($entries | Where-Object { $_ -like 'analyzers/dotnet/cs/*' }) `
                @('analyzers/dotnet/cs/Kevlar.Analyzers.dll', 'analyzers/dotnet/cs/Kevlar.Analyzers.pdb')
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
            if ($packageId -in @('Kevlar.Chaos', 'Kevlar.Testing'))
            {
                $expectedAssets += @(
                    "lib/net8.0/$packageId.dll",
                    "lib/net8.0/$packageId.xml"
                )
            }
            elseif ($packageId -eq 'Kevlar.Extensions.Grpc')
            {
                $expectedAssets += @(
                    "lib/netstandard2.1/$packageId.dll",
                    "lib/netstandard2.1/$packageId.xml"
                )
            }
            Assert-Set "$packageId library assets" ($entries | Where-Object { $_ -like 'lib/*' }) $expectedAssets
            if ($entries | Where-Object { $_ -match '^(analyzers|build|buildTransitive|tools)/' })
            {
                throw "$packageId contains an unexpected analyzer or build asset."
            }
        }

        foreach ($assemblyEntry in $archive.Entries | Where-Object { $_.FullName -like '*.dll' })
        {
            Assert-DeterministicAssembly "$packageId $($assemblyEntry.FullName)" $assemblyEntry
        }
    }
    finally
    {
        $archive.Dispose()
    }

    $symbolArchive = [System.IO.Compression.ZipFile]::OpenRead($symbolPackageFile)
    try
    {
        $symbolEntries = @($symbolArchive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        Assert-Set "$packageId symbol assets" `
            ($symbolEntries | Where-Object { $_ -like '*.pdb' }) `
            (Get-ExpectedSymbolAssets $packageId)

        $symbolNuspecEntry = @($symbolArchive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
        Assert-Equal "$packageId symbol nuspec count" $symbolNuspecEntry.Count 1
        $symbolNuspecReader = [System.IO.StreamReader]::new($symbolNuspecEntry[0].Open())
        try
        {
            [xml]$symbolNuspec = $symbolNuspecReader.ReadToEnd()
        }
        finally
        {
            $symbolNuspecReader.Dispose()
        }

        $symbolMetadata = $symbolNuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        if ($null -eq $symbolMetadata)
        {
            throw "$packageId symbol package has no nuspec metadata."
        }

        Assert-Equal "$packageId symbol ID" (Get-NodeText $symbolMetadata "*[local-name()='id']") $packageId
        Assert-Equal "$packageId symbol version" (Get-NodeText $symbolMetadata "*[local-name()='version']") $Version
        Assert-Equal "$packageId symbol package type" `
            (Get-NodeText $symbolMetadata "*[local-name()='packageTypes']/*[local-name()='packageType']/@name") `
            'SymbolsPackage'
    }
    finally
    {
        $symbolArchive.Dispose()
    }
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "kevlar-package-consumers-$([guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
$previousPackagesPath = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = Join-Path $temporaryRoot '.packages'

try
{
    $symbolRoot = Join-Path $temporaryRoot 'symbols'
    [System.IO.Directory]::CreateDirectory($symbolRoot) | Out-Null
    foreach ($packageId in $expectedDependencies.Keys)
    {
        $symbolPackageFile = Join-Path $packageDirectory "$packageId.$Version.snupkg"
        $symbolArchive = [System.IO.Compression.ZipFile]::OpenRead($symbolPackageFile)
        try
        {
            foreach ($symbolAsset in Get-ExpectedSymbolAssets $packageId)
            {
                $entry = $symbolArchive.GetEntry($symbolAsset)
                if ($null -eq $entry)
                {
                    throw "$packageId symbol package does not contain '$symbolAsset'."
                }

                $pdbPath = Join-Path $symbolRoot $symbolAsset
                [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($pdbPath)) | Out-Null
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $pdbPath, $true)

                $sourceLinkJson = (& dotnet sourcelink print-json $pdbPath 2>&1 | Out-String)
                if ($LASTEXITCODE -ne 0)
                {
                    throw "Unable to read SourceLink data from '$symbolAsset'.`n$sourceLinkJson"
                }

                $sourceLink = $sourceLinkJson | ConvertFrom-Json
                Assert-Set `
                    "$symbolAsset SourceLink URLs" `
                    @($sourceLink.documents.PSObject.Properties.Value) `
                    @("https://raw.githubusercontent.com/thomhurst/Kevlar/$expectedRepositoryCommit/*")

                Invoke-DotNet @('sourcelink', 'test', $pdbPath)
            }
        }
        finally
        {
            $symbolArchive.Dispose()
        }
    }

    $repeatPackages = Join-Path $temporaryRoot 'repeat-packages'
    Invoke-DotNet @(
        'build', (Join-Path $repositoryRoot 'Kevlar.slnx'),
        '-c', 'Release',
        '--no-restore',
        '-t:Rebuild',
        "-p:Version=$Version",
        '-p:CI=true')
    Invoke-DotNet @(
        'pack', (Join-Path $repositoryRoot 'Kevlar.slnx'),
        '-c', 'Release',
        '--no-build',
        "-p:Version=$Version",
        '-p:CI=true',
        "-p:PackageOutputPath=$repeatPackages")

    foreach ($packageId in $expectedDependencies.Keys)
    {
        $packageFile = Join-Path $packageDirectory "$packageId.$Version.nupkg"
        $repeatPackageFile = Join-Path $repeatPackages "$packageId.$Version.nupkg"
        foreach ($assemblyAsset in (Get-ExpectedSymbolAssets $packageId) -replace '\.pdb$', '.dll')
        {
            Assert-Equal `
                "$packageId deterministic assembly $assemblyAsset" `
                (Get-ArchiveEntryHash $repeatPackageFile $assemblyAsset) `
                (Get-ArchiveEntryHash $packageFile $assemblyAsset)
        }
    }

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
using Kevlar.Chaos;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Grpc;
using Kevlar.Extensions.Http;
using Kevlar.Extensions.RateLimiting;
using Kevlar.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;

var shield = Shield.Empty;
var descriptor = shield.GetDescriptor();
descriptor.AssertStrategyCount(0);
var value = await shield.ExecuteAsync(static cancellationToken =>
    new ValueTask<int>(cancellationToken.CanBeCanceled ? 41 : 42));
if (value != 42)
{
    throw new InvalidOperationException("Core package execution failed.");
}

try
{
    ThrowFromKevlar();
    throw new InvalidOperationException("Expected the packaged shield to throw.");
}
catch (ExpectedConsumerException exception)
{
    if (exception.StackTrace is null || !exception.StackTrace.Contains("Kevlar/Internal/ShieldEngine.cs:line ", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Packaged symbols did not produce Kevlar source line information:{Environment.NewLine}{exception.StackTrace}");
    }
}

var injections = 0;
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, activeListener) =>
{
    if (instrument.Meter.Name == ChaosDiagnostics.MeterName
        && instrument.Name == "kevlar.chaos.injections")
    {
        activeListener.EnableMeasurementEvents(instrument);
    }
};
listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => injections += (int)measurement);
listener.Start();

var chaos = ChaosShield.Outcome<int>(options =>
{
    options.Enabled = true;
    options.Result = 42;
});
if (await chaos.ExecuteAsync(static _ => new ValueTask<int>(0)) != 42)
{
    throw new InvalidOperationException("Chaos package execution failed.");
}
if (injections != 1)
{
    throw new InvalidOperationException($"Chaos package metrics failed: expected 1 injection, actual {injections}.");
}

IServiceCollection services = new ServiceCollection();
services.AddShield("consumer", shield);
services.AddHttpClient("consumer").AddStandardShield();
_ = new ShieldUnaryClientInterceptor(GrpcShield.WhenTransient().Retry(1));
using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
{
    PermitLimit = 1,
    QueueLimit = 0,
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
});
if (await Shield.Empty.RateLimit(limiter).ExecuteAsync(static _ => new ValueTask<int>(42)) != 42)
{
    throw new InvalidOperationException("Rate limiting adapter execution failed.");
}
Console.WriteLine("Kevlar package consumer passed.");

[MethodImpl(MethodImplOptions.NoInlining)]
static void ThrowFromKevlar() =>
    Shield.Retry(0).Execute(static _ => throw new ExpectedConsumerException());

sealed class ExpectedConsumerException : Exception;
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
    <PackageReference Include="Kevlar.Chaos" Version="$Version" />
    <PackageReference Include="Kevlar.Extensions.DependencyInjection" Version="$Version" />
    <PackageReference Include="Kevlar.Extensions.Http" Version="$Version" />
    <PackageReference Include="Kevlar.Extensions.RateLimiting" Version="$Version" />
    <PackageReference Include="Kevlar.Testing" Version="$Version" />
    <PackageReference Include="Kevlar.Extensions.Grpc" Version="$Version" />
  </ItemGroup>
</Project>
"@
        $projectPath = Join-Path $consumerDirectory 'Consumer.csproj'
        Write-TextFile $projectPath $consumerProject
        Write-TextFile (Join-Path $consumerDirectory 'Program.cs') $runtimeProgram
        Invoke-DotNet @('restore', $projectPath, '--configfile', $nugetConfigPath, '--no-cache', '--force-evaluate')
        Invoke-DotNet @('build', $projectPath, '-c', 'Release', '--no-restore')
        $kevlarPdbFramework = if ($framework -eq 'net10.0') { 'net10.0' } else { 'netstandard2.0' }
        Copy-Item `
            -LiteralPath (Join-Path $symbolRoot "lib/$kevlarPdbFramework/Kevlar.pdb") `
            -Destination (Join-Path $consumerDirectory "bin/Release/$framework/Kevlar.pdb")
        Invoke-DotNet @('run', '--project', $projectPath, '-c', 'Release', '--no-build', '--no-restore')
    }

    & (Join-Path $PSScriptRoot 'Verify-PublishCompatibility.ps1') `
        -PackagesPath $packageDirectory `
        -Version $Version `
        -ConfigurationVersion $configurationVersion `
        -DependencyInjectionVersion $dependencyInjectionVersion

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

    Write-Host 'All package layout, symbols, determinism, SourceLink, consumer, and analyzer checks passed.'
}
finally
{
    $env:NUGET_PACKAGES = $previousPackagesPath
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
}

# The analyzer check intentionally runs a failing dotnet build. Do not leak that
# expected native exit code after every assertion and cleanup step has passed.
exit 0
