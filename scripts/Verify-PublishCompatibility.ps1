[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagesPath,

    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$ConfigurationVersion,

    [Parameter(Mandatory)]
    [string]$DependencyInjectionVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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

function Get-RuntimeIdentifier
{
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $architectureName = switch ($architecture)
    {
        ([System.Runtime.InteropServices.Architecture]::X64) { 'x64' }
        ([System.Runtime.InteropServices.Architecture]::Arm64) { 'arm64' }
        default { throw "Publish compatibility does not support architecture '$architecture'." }
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows))
    {
        return "win-$architectureName"
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux))
    {
        return "linux-$architectureName"
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX))
    {
        return "osx-$architectureName"
    }

    throw 'Publish compatibility does not support this operating system.'
}

$packageDirectory = (Resolve-Path -LiteralPath $PackagesPath).Path
$runtimeIdentifier = Get-RuntimeIdentifier
$platformName = $runtimeIdentifier.Split('-')[0]
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "kevlar-publish-consumer-$([guid]::NewGuid().ToString('N'))"
$consumerDirectory = Join-Path $temporaryRoot 'consumer'
$projectPath = Join-Path $consumerDirectory 'PublishConsumer.csproj'
$nugetConfigPath = Join-Path $temporaryRoot 'NuGet.Config'
$previousPackagesPath = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = Join-Path $temporaryRoot '.packages'

try
{
    $escapedPackageDirectory = [System.Security.SecurityElement]::Escape($packageDirectory)
    Write-TextFile $nugetConfigPath @"
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

    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
    <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
    <SuppressTrimAnalysisWarnings>false</SuppressTrimAnalysisWarnings>
    <TrimmerSingleWarn>false</TrimmerSingleWarn>
    <ILLinkTreatWarningsAsErrors>true</ILLinkTreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kevlar" Version="$Version" />
    <PackageReference Include="Kevlar.Chaos" Version="$Version" />
    <PackageReference Include="Kevlar.Extensions.DependencyInjection" Version="$Version" />
    <PackageReference Include="Kevlar.Extensions.Http" Version="$Version" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="$ConfigurationVersion" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="$DependencyInjectionVersion" />
  </ItemGroup>
</Project>
"@
    Write-TextFile $projectPath $project
    Write-TextFile (Join-Path $consumerDirectory 'Program.cs') @'
using System.Net;
using Kevlar;
using Kevlar.Chaos;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var untyped = Shield.Empty;
if (untyped.Execute(static _ => 42) != 42
    || await untyped.ExecuteAsync(static _ => new ValueTask<int>(42)) != 42
    || !(await untyped.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42))).IsSuccess)
{
    throw new InvalidOperationException("Untyped execution failed.");
}

var typed = Shield<int>.Empty;
if (typed.Execute(static _ => 42) != 42
    || await typed.ExecuteAsync(static _ => new ValueTask<int>(42)) != 42)
{
    throw new InvalidOperationException("Typed execution failed.");
}

var chaos = ChaosShield.Outcome<int>(options =>
{
    options.Enabled = true;
    options.Result = 42;
});
if (await chaos.ExecuteAsync(static _ => new ValueTask<int>(0)) != 42)
{
    throw new InvalidOperationException("Chaos package execution failed.");
}

var retryAttempts = 0;
var retried = await Shield.Retry(1, Backoff.None).ExecuteAsync(_ =>
    ++retryAttempts == 1 ? throw new InvalidOperationException() : new ValueTask<int>(42));
var timed = await Shield.Timeout(TimeSpan.FromSeconds(1)).ExecuteAsync(static _ => new ValueTask<int>(42));
var broken = await Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).ExecuteAsync(static _ => new ValueTask<int>(42));
var limited = await Shield.RateLimit(1, TimeSpan.FromSeconds(1)).ExecuteAsync(static _ => new ValueTask<int>(42));
var isolated = await Shield.ConcurrencyLimit(1).ExecuteAsync(static _ => new ValueTask<int>(42));
var hedged = await Shield.Hedge(2, TimeSpan.FromSeconds(1)).ExecuteAsync(static _ => new ValueTask<int>(42));
var fallback = await Shield.For<int>().When<InvalidOperationException>().Fallback(42)
    .ExecuteAsync<int>(static _ => throw new InvalidOperationException());
if (retried + timed + broken + limited + isolated + hedged + fallback != 294)
{
    throw new InvalidOperationException("Built-in strategy execution failed.");
}

var settings = new Dictionary<string, string?>
{
    ["Retry:MaxRetries"] = "1",
    ["Retry:Backoff"] = "None",
    ["CircuitBreaker:ConsecutiveFailures"] = "2",
    ["CircuitBreaker:BreakDuration"] = "00:00:01",
    ["RateLimit:Permits"] = "10",
    ["RateLimit:Window"] = "00:00:01",
    ["ConcurrencyLimit:MaxConcurrency"] = "2",
};
var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
var services = new ServiceCollection();
services.AddShield("configured", configuration);
services.AddShield<int>("typed", typed);
services.AddHttpClient("http")
    .ConfigurePrimaryHttpMessageHandler(static () => new StubHandler())
    .AddShield(HttpShield.WhenTransient().Retry(1, Backoff.None));
using var provider = services.BuildServiceProvider();
var registry = provider.GetRequiredService<IKevlarRegistry>();
if (await registry.GetShield("configured").ExecuteAsync(static _ => new ValueTask<int>(42)) != 42
    || await registry.GetShield<int>("typed").ExecuteAsync(static _ => new ValueTask<int>(42)) != 42)
{
    throw new InvalidOperationException("Dependency-injection registration failed.");
}

using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("http");
using var response = await client.GetAsync("http://localhost/smoke");
if (response.StatusCode != HttpStatusCode.OK)
{
    throw new InvalidOperationException("HTTP handler wiring failed.");
}

Console.WriteLine("Kevlar publish compatibility passed.");

file sealed class StubHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
}
'@

    $publishMatrix = @(
        @{ Name = 'net10-trimmed'; Framework = 'net10.0'; Platforms = @('win', 'linux', 'osx'); Properties = @('-p:PublishTrimmed=true', '-p:TrimMode=full') },
        @{ Name = 'net10-single-file'; Framework = 'net10.0'; Platforms = @('win', 'linux', 'osx'); Properties = @('-p:PublishSingleFile=true') },
        @{ Name = 'net10-native-aot'; Framework = 'net10.0'; Platforms = @('linux'); Properties = @('-p:PublishAot=true') },
        @{ Name = 'net8-compat-trimmed'; Framework = 'net8.0'; Platforms = @('win', 'linux', 'osx'); Properties = @('-p:PublishTrimmed=true', '-p:TrimMode=full') }
    )

    foreach ($entry in $publishMatrix)
    {
        if ($entry.Platforms -notcontains $platformName)
        {
            Write-Host "$($entry.Name): explicitly unsupported on $platformName; NativeAOT package verification runs on Linux CI."
            continue
        }

        $outputDirectory = Join-Path $temporaryRoot $entry.Name
        $arguments = @(
            'publish', $projectPath,
            '-c', 'Release',
            '-f', $entry.Framework,
            '-r', $runtimeIdentifier,
            '--self-contained', 'true',
            '--configfile', $nugetConfigPath,
            '--no-cache',
            '-o', $outputDirectory
        ) + $entry.Properties
        Invoke-DotNet $arguments

        $executableName = if ($runtimeIdentifier.StartsWith('win-', [StringComparison]::Ordinal))
        {
            'PublishConsumer.exe'
        }
        else
        {
            'PublishConsumer'
        }
        $executable = Join-Path $outputDirectory $executableName
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf))
        {
            throw "$($entry.Name) did not produce '$executableName'."
        }

        & $executable
        if ($LASTEXITCODE -ne 0)
        {
            throw "$($entry.Name) executable failed with exit code $LASTEXITCODE."
        }
    }

    Write-Host "All publish compatibility checks passed for $runtimeIdentifier."
}
finally
{
    $env:NUGET_PACKAGES = $previousPackagesPath
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
}
