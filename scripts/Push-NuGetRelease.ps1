[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagesPath,

    [Parameter(Mandatory)]
    [string]$ApiKey,

    [string]$Source = 'https://api.nuget.org/v3/index.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression.FileSystem

$resolvedPackagesPath = (Resolve-Path -LiteralPath $PackagesPath).Path
$serviceIndex = Invoke-RestMethod -Uri $Source
$packageBaseAddress = $serviceIndex.resources |
    Where-Object { $_.'@type' -like 'PackageBaseAddress/*' } |
    Select-Object -ExpandProperty '@id' -First 1
if (-not $packageBaseAddress)
{
    throw "Package source '$Source' has no PackageBaseAddress resource."
}

$packages = @(Get-ChildItem -LiteralPath $resolvedPackagesPath -File -Filter '*.nupkg' | Sort-Object Name)
$symbols = @(Get-ChildItem -LiteralPath $resolvedPackagesPath -File -Filter '*.snupkg' | Sort-Object Name)
if ($packages.Count -eq 0)
{
    throw "No NuGet packages were found in '$resolvedPackagesPath'."
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "kevlar-nuget-publish-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
$client = [Net.Http.HttpClient]::new()

try
{
    foreach ($package in $packages)
    {
        $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
        try
        {
            $nuspec = $archive.Entries | Where-Object { $_.Name.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
            if (-not $nuspec)
            {
                throw "Package '$($package.Name)' has no nuspec."
            }

            $reader = [IO.StreamReader]::new($nuspec.Open())
            try
            {
                [xml]$metadata = $reader.ReadToEnd()
            }
            finally
            {
                $reader.Dispose()
            }

            $id = [string]$metadata.package.metadata.id
            $version = [string]$metadata.package.metadata.version
        }
        finally
        {
            $archive.Dispose()
        }

        $lowerId = $id.ToLowerInvariant()
        $lowerVersion = $version.ToLowerInvariant()
        $publishedUri = "$($packageBaseAddress.TrimEnd('/'))/$lowerId/$lowerVersion/$lowerId.$lowerVersion.nupkg"
        $response = $client.GetAsync($publishedUri).GetAwaiter().GetResult()
        try
        {
            if ($response.StatusCode -eq [Net.HttpStatusCode]::NotFound)
            {
                & dotnet nuget push $package.FullName --no-symbols --api-key $ApiKey --source $Source
                if ($LASTEXITCODE -ne 0)
                {
                    throw "Publishing '$($package.Name)' failed."
                }

                continue
            }

            if (-not $response.IsSuccessStatusCode)
            {
                throw "Could not inspect '$id' $version on '$Source': HTTP $([int]$response.StatusCode)."
            }

            $publishedPath = Join-Path $temporaryRoot $package.Name
            $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            [IO.File]::WriteAllBytes($publishedPath, $bytes)
            & (Join-Path $PSScriptRoot 'Compare-NuGetPackagePayload.ps1') `
                -ExpectedPath $package.FullName `
                -ActualPath $publishedPath
            Write-Host "$id $version already has the same payload; skipping."
        }
        finally
        {
            $response.Dispose()
        }
    }

    foreach ($symbol in $symbols)
    {
        # NuGet.org accepts repeated symbol-package submissions for the same ID/version.
        & dotnet nuget push $symbol.FullName --api-key $ApiKey --source $Source
        if ($LASTEXITCODE -ne 0)
        {
            throw "Publishing '$($symbol.Name)' failed."
        }
    }
}
finally
{
    $client.Dispose()
    [IO.Directory]::Delete($temporaryRoot, $true)
}
