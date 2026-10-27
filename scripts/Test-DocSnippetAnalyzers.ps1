[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $repositoryRoot "artifacts/doc-analyzer-tests/$([Guid]::NewGuid().ToString('N'))"

# Load the verifier's functions without restoring packages or generating snippets.
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'Verify-DocSnippets.ps1'), [ref]$null, [ref]$parseErrors)
if ($parseErrors.Count -gt 0)
{
    throw ($parseErrors | Out-String)
}
foreach ($name in @('Test-PathWithinDirectory', 'Get-FrameworkAnalyzerPaths', 'Assert-EffectiveAnalyzerConfiguration'))
{
    $definition = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $definition)
    {
        throw "Missing verifier function $name."
    }
    . ([scriptblock]::Create($definition.Extent.Text))
}

# Exercise the actual configuration guard with deterministic SDK query results.
function dotnet
{
    if ('-getItem:CscCommandLineArgs,ResolvedFrameworkReference' -notin $args)
    {
        throw 'The verifier must query the active framework references.'
    }
    $global:LASTEXITCODE = 0
    $script:configuration | ConvertTo-Json -Depth 10
}

function New-TestPack([string]$RelativeDirectory, [string]$FrameworkName)
{
    $packDirectory = Join-Path $testRoot $RelativeDirectory
    $manifestPath = Join-Path $packDirectory 'data/FrameworkList.xml'
    $analyzerPath = Join-Path $packDirectory 'analyzers/dotnet/cs/Framework.Generator.dll'
    [void][IO.Directory]::CreateDirectory((Split-Path $manifestPath -Parent))
    [void][IO.Directory]::CreateDirectory((Split-Path $analyzerPath -Parent))
    [IO.File]::WriteAllText($analyzerPath, '')
    [IO.File]::WriteAllText($manifestPath, @"
<FileList FrameworkName="$FrameworkName">
  <File Type="Analyzer" Path="analyzers/dotnet/cs/Framework.Generator.dll" Language="cs" />
  <File Type="Managed" Path="ref/net8.0/Framework.dll" />
</FileList>
"@)
    [pscustomobject]@{
        Identity = $FrameworkName
        TargetingPackName = "$FrameworkName.Ref"
        TargetingPackPath = $packDirectory
    }
}

function Assert-Configuration([string]$Name, [object[]]$Packs, [string[]]$Analyzers, [string]$ExpectedError = '')
{
    $script:configuration = @{
        Properties = @{
            Configuration = 'Release'
            TargetFramework = 'net8.0'
            MSBuildToolsPath = $sdkDirectory
        }
        Items = @{
            ResolvedFrameworkReference = $Packs
            CscCommandLineArgs = @(
                '/warn:9999', '/warnaserror+', '/nullable:enable'
                "/analyzer:$sdkDirectory/Sdks/Microsoft.NET.Sdk/analyzers/Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll"
                "/analyzer:$sdkDirectory/Sdks/Microsoft.NET.Sdk/analyzers/Microsoft.CodeAnalysis.NetAnalyzers.dll"
                $Analyzers | ForEach-Object { "/analyzer:$_" }
            ) | ForEach-Object { @{ Identity = $_ } }
        }
    }
    $failure = $null
    try
    {
        Assert-EffectiveAnalyzerConfiguration -Framework net8.0
    }
    catch
    {
        $failure = $_.Exception.Message
    }
    if ($ExpectedError)
    {
        if (-not $failure -or -not $failure.Contains($ExpectedError))
        {
            throw "$Name expected '$ExpectedError'; got '$failure'."
        }
    }
    elseif ($failure)
    {
        throw "$Name unexpectedly failed: $failure"
    }
    Write-Host "PASS: $Name"
}

$sdkDirectory = Join-Path $testRoot 'dotnet/sdk/10.0.403'
$projectPath = Join-Path $testRoot 'Documentation.csproj'
$warningBuildPropertyValues = [ordered]@{}
$documentationBuildProperties = @()
$allowedBaselineNoWarnIds = @('1701', '1702', '8002')
$pathComparer = if ([OperatingSystem]::IsWindows()) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
$trustedPackageAnalyzerPaths = [Collections.Generic.HashSet[string]]::new($pathComparer)

try
{
    $installed = New-TestPack 'dotnet/packs/Microsoft.NETCore.App.Ref/8.0.33' 'Microsoft.NETCore.App'
    $aspnet = New-TestPack 'dotnet/packs/Microsoft.AspNetCore.App.Ref/8.0.33' 'Microsoft.AspNetCore.App'
    $nuget = New-TestPack 'nuget/microsoft.netcore.app.ref/8.0.33' 'Microsoft.NETCore.App'
    $fallback = New-TestPack 'custom-cache/microsoft.aspnetcore.app.ref/8.0.33' 'Microsoft.AspNetCore.App'
    $analyzerSuffix = 'analyzers/dotnet/cs/Framework.Generator.dll'
    $installedAnalyzer = Join-Path $installed.TargetingPackPath $analyzerSuffix
    $nugetAnalyzer = Join-Path $nuget.TargetingPackPath $analyzerSuffix
    Assert-Configuration 'installed framework packs' @($installed, $aspnet) @($installedAnalyzer, (Join-Path $aspnet.TargetingPackPath $analyzerSuffix))
    Assert-Configuration 'NuGet and custom fallback packs' @($nuget, $fallback) @($nugetAnalyzer, (Join-Path $fallback.TargetingPackPath $analyzerSuffix))
    Assert-Configuration 'unselected installed pack' @($nuget) @($installedAnalyzer) 'unexpected analyzer'
    Assert-Configuration 'unselected NuGet pack' @($installed) @($nugetAnalyzer) 'unexpected analyzer'
    Assert-Configuration 'no selected packs' @() @($nugetAnalyzer) 'unexpected analyzer'
    $caseError = if ([OperatingSystem]::IsWindows()) { '' } else { 'unexpected analyzer' }
    Assert-Configuration 'platform path case comparison' @($nuget) @($nugetAnalyzer.ToUpperInvariant()) $caseError
    $trustedAnalyzer = Join-Path $testRoot 'nuget/kevlar/1.0/analyzers/dotnet/cs/Kevlar.Analyzers.dll'
    [void]$trustedPackageAnalyzerPaths.Add($trustedAnalyzer)
    Assert-Configuration 'existing trusted package analyzer' @() @($trustedAnalyzer)
    foreach ($pack in @($installed, $nuget))
    {
        $injected = Join-Path $pack.TargetingPackPath 'analyzers/dotnet/cs/Injected.dll'
        [IO.File]::WriteAllText($injected, '')
        Assert-Configuration "unlisted analyzer in $($pack.TargetingPackPath)" @($pack) @($injected) 'unexpected analyzer'
        Assert-Configuration "non-analyzer manifest entry in $($pack.TargetingPackPath)" @($pack) @((Join-Path $pack.TargetingPackPath 'ref/net8.0/Framework.dll')) 'unexpected analyzer'
    }
    Assert-Configuration 'sibling directory prefix' @($nuget) @((Join-Path "$($nuget.TargetingPackPath)-injected" $analyzerSuffix)) 'unexpected analyzer'
    Assert-Configuration 'unrelated NuGet package' @($nuget) @((Join-Path $testRoot "nuget/unrelated/8.0.33/$analyzerSuffix")) 'unexpected analyzer'

    $untrusted = New-TestPack 'nuget/untrusted/1.0' 'Untrusted.App'
    Assert-Configuration 'unapproved framework identity' @($untrusted) @() 'Unexpected documentation targeting pack'
    $untrusted.Identity = 'Microsoft.NETCore.App'
    Assert-Configuration 'mismatched package identity' @($untrusted) @() 'Unexpected documentation targeting pack'
    $untrusted.TargetingPackName = 'Microsoft.NETCore.App.Ref'
    Assert-Configuration 'mismatched manifest identity' @($untrusted) @() 'unexpected framework'
    $untrusted.TargetingPackPath = 'relative/path'
    Assert-Configuration 'relative pack path' @($untrusted) @() 'no absolute resolved path'

    $manifestPath = Join-Path $nuget.TargetingPackPath 'data/FrameworkList.xml'
    [IO.File]::WriteAllText($manifestPath, '<FileList FrameworkName="Microsoft.NETCore.App"><File Type="Analyzer" Path="analyzers/../../../Injected.dll" /></FileList>')
    Assert-Configuration 'manifest traversal' @($nuget) @() 'invalid analyzer'
    [IO.File]::WriteAllText($manifestPath, '<FileList FrameworkName="Microsoft.NETCore.App"><File Type="Analyzer" Path="analyzers/Missing.dll" /></FileList>')
    Assert-Configuration 'missing analyzer file' @($nuget) @() 'invalid analyzer'
    Remove-Item -LiteralPath $manifestPath
    Assert-Configuration 'missing manifest' @($nuget) @() 'does not exist'
}
finally
{
    # The path is a unique, absolute child of this worktree's artifacts directory.
    $artifactsDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
    if (-not (Test-PathWithinDirectory -Path ([IO.Path]::GetFullPath($testRoot)) -Directory $artifactsDirectory))
    {
        throw "Test cleanup path escaped artifacts: $testRoot"
    }
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
