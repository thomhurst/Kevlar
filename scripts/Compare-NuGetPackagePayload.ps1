[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExpectedPath,

    [Parameter(Mandatory)]
    [string]$ActualPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-PayloadHashes([string]$packagePath)
{
    $hashes = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($packagePath))
    try
    {
        foreach ($entry in $archive.Entries)
        {
            if (-not $entry.Name -or $entry.FullName.Equals('.signature.p7s', [StringComparison]::OrdinalIgnoreCase))
            {
                continue
            }

            $stream = $entry.Open()
            try
            {
                $hash = [Security.Cryptography.SHA256]::HashData($stream)
                $hashes.Add($entry.FullName, [Convert]::ToHexString($hash))
            }
            finally
            {
                $stream.Dispose()
            }
        }
    }
    finally
    {
        $archive.Dispose()
    }

    return $hashes
}

$expected = Get-PayloadHashes $ExpectedPath
$actual = Get-PayloadHashes $ActualPath
$differences = [Collections.Generic.List[string]]::new()

foreach ($entry in $expected.Keys)
{
    if (-not $actual.ContainsKey($entry))
    {
        $differences.Add("missing '$entry'")
    }
    elseif ($actual[$entry] -ne $expected[$entry])
    {
        $differences.Add("different '$entry'")
    }
}

foreach ($entry in $actual.Keys)
{
    if (-not $expected.ContainsKey($entry))
    {
        $differences.Add("unexpected '$entry'")
    }
}

if ($differences.Count -gt 0)
{
    throw "Published package payload conflicts with the release artifact: $($differences -join ', ')."
}
