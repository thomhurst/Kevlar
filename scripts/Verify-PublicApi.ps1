[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagesPath,

    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),

    [string]$BaselineRoot = (Join-Path $RepositoryRoot 'src')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$packageDirectory = (Resolve-Path -LiteralPath $PackagesPath).Path
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "kevlar-public-api-$([Guid]::NewGuid().ToString('N'))"
$resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
[IO.Directory]::CreateDirectory($resolvedTemporaryRoot) | Out-Null

try
{
    $assemblies = [Collections.Generic.List[object]]::new()
    foreach ($packagePath in Get-ChildItem -LiteralPath $packageDirectory -File -Filter '*.nupkg')
    {
        $packageId = $packagePath.BaseName -replace '\.\d+\.\d+\.\d+(?:[-+].*)?$', ''
        $archive = [IO.Compression.ZipFile]::OpenRead($packagePath.FullName)
        try
        {
            foreach ($entry in $archive.Entries)
            {
                $assemblyId = $null
                $targetFramework = $null
                if ($entry.FullName -match '^lib/([^/]+)/([^/]+)\.dll$' -and $Matches[2] -eq $packageId)
                {
                    $assemblyId = $packageId
                    $targetFramework = $Matches[1].ToLowerInvariant()
                }
                elseif ($entry.FullName -match '^analyzers/dotnet/cs/(Kevlar\.Analyzers(?:\.CodeFixes)?)\.dll$')
                {
                    $assemblyId = $Matches[1]
                    $targetFramework = 'netstandard2.0'
                }
                else
                {
                    continue
                }

                $outputDirectory = Join-Path $resolvedTemporaryRoot $targetFramework
                [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
                $assemblyPath = Join-Path $outputDirectory "$assemblyId.dll"
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $assemblyPath, $true)
                $assemblies.Add([pscustomobject]@{
                    AssemblyId = $assemblyId
                    TargetFramework = $targetFramework
                    AssemblyPath = $assemblyPath
                })
            }
        }
        finally
        {
            $archive.Dispose()
        }
    }

    if ($assemblies.Count -eq 0)
    {
        throw "No package assemblies were found in '$packageDirectory'."
    }

    $toolProject = Join-Path $RepositoryRoot 'tools/Kevlar.ApiVerifier/Kevlar.ApiVerifier.csproj'
    & dotnet build $toolProject -c Release --nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not build $toolProject."
    }

    $referenceRoots = @(
        $resolvedTemporaryRoot,
        (Join-Path $RepositoryRoot 'artifacts/bin'))
    foreach ($assembly in $assemblies)
    {
        $baselineDirectory = Join-Path $BaselineRoot $assembly.AssemblyId
        $baselines = [Collections.Generic.List[string]]::new()
        $commonBaseline = Join-Path $baselineDirectory 'PublicAPI.Shipped.txt'
        if (-not (Test-Path -LiteralPath $commonBaseline -PathType Leaf))
        {
            throw "Missing shipped baseline '$commonBaseline'."
        }

        $baselines.Add($commonBaseline)
        $frameworkBaseline = Join-Path $baselineDirectory "PublicAPI.Shipped.$($assembly.TargetFramework).txt"
        if (Test-Path -LiteralPath $frameworkBaseline -PathType Leaf)
        {
            $baselines.Add($frameworkBaseline)
        }

        $arguments = [Collections.Generic.List[string]]::new()
        foreach ($argument in @(
            'run',
            '--project', $toolProject,
            '-c', 'Release',
            '--no-build',
            '--',
            '--assembly', $assembly.AssemblyPath,
            '--tfm', $assembly.TargetFramework))
        {
            $arguments.Add($argument)
        }
        foreach ($baseline in $baselines)
        {
            $arguments.Add('--baseline')
            $arguments.Add($baseline)
        }

        foreach ($referenceRoot in $referenceRoots)
        {
            $arguments.Add('--reference-root')
            $arguments.Add($referenceRoot)
        }

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0)
        {
            throw "$($assembly.AssemblyId) $($assembly.TargetFramework) public API verification failed."
        }
    }

    Write-Host "Packed public APIs match shipped baselines for $($assemblies.Count) assemblies."
}
finally
{
    if ($resolvedTemporaryRoot.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase))
    {
        [IO.Directory]::Delete($resolvedTemporaryRoot, $true)
    }
}
