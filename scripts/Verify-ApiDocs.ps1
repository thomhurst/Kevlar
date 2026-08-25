[CmdletBinding()]
param(
    [string]$MetadataPath,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
if (-not $MetadataPath)
{
    $MetadataPath = Join-Path $repositoryRoot 'docs/api'
}

if (-not $OutputPath)
{
    $OutputPath = Join-Path $repositoryRoot 'docs/static/api'
}

$resolvedMetadataPath = (Resolve-Path -LiteralPath $MetadataPath).Path
$resolvedOutputPath = (Resolve-Path -LiteralPath $OutputPath).Path
$uids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$errors = [System.Collections.Generic.List[string]]::new()

foreach ($metadataFile in Get-ChildItem -LiteralPath $resolvedMetadataPath -Filter '*.yml' -File)
{
    foreach ($line in Get-Content -LiteralPath $metadataFile.FullName)
    {
        if ($line -match '^\s*-?\s*uid:\s*(?<uid>\S+)\s*$')
        {
            [void]$uids.Add($Matches.uid)
        }
    }
}

$publicApiFiles = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Filter 'PublicAPI.*.txt' -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/]Kevlar\.Analyzers[\\/]' }
$publicTypes = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$removedTypes = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$removedApiPrefix = '*REMOVED*'

function ConvertTo-DocFxTypeUid([string]$typeDeclaration)
{
    if ($typeDeclaration -match '^(?<name>[^<]+)<(?<arguments>[^>]+)>$')
    {
        $arity = $Matches.arguments.Split(',').Count
        return "$($Matches.name)``$arity"
    }

    return $typeDeclaration
}

foreach ($publicApiFile in $publicApiFiles)
{
    foreach ($line in Get-Content -LiteralPath $publicApiFile.FullName)
    {
        if ($line.StartsWith($removedApiPrefix, [StringComparison]::Ordinal))
        {
            $removedDeclaration = $line.Substring($removedApiPrefix.Length)
            if ($removedDeclaration -and
                -not $removedDeclaration.Contains(' -> ', [StringComparison]::Ordinal) -and
                -not $removedDeclaration.Contains(' = ', [StringComparison]::Ordinal))
            {
                [void]$removedTypes.Add((ConvertTo-DocFxTypeUid $removedDeclaration))
            }

            continue
        }

        if (-not $line -or
            $line.StartsWith('#', [StringComparison]::Ordinal) -or
            $line.Contains(' -> ', [StringComparison]::Ordinal) -or
            $line.Contains(' = ', [StringComparison]::Ordinal))
        {
            continue
        }

        [void]$publicTypes.Add((ConvertTo-DocFxTypeUid $line.Trim()))
    }
}

foreach ($removedType in $removedTypes)
{
    [void]$publicTypes.Remove($removedType)
}

foreach ($publicType in $publicTypes)
{
    if (-not $uids.Contains($publicType))
    {
        $errors.Add("Public type '$publicType' has no DocFX metadata entry.")
    }
}

$apiLinks = [regex]::Matches(
    (Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs/docs') -Filter '*.md' -File -Recurse |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n",
    '\]\((?:pathname://)?(?:/Kevlar)?/api/(?<page>[^)#?]+\.html)(?:#[^)]+)?\)')

foreach ($link in $apiLinks)
{
    $target = Join-Path $resolvedOutputPath $link.Groups['page'].Value
    if (-not (Test-Path -LiteralPath $target -PathType Leaf))
    {
        $errors.Add("API link target does not exist: $($link.Value)")
    }
}

$shieldPage = Join-Path $resolvedOutputPath 'Kevlar.Shield.html'
if (-not (Test-Path -LiteralPath $shieldPage -PathType Leaf))
{
    $errors.Add('Generated API site has no Kevlar.Shield.html page.')
}
elseif ((Get-Content -LiteralPath $shieldPage -Raw) -notmatch 'data-uid="Kevlar\.Shield\.Retry')
{
    $errors.Add('Generated Kevlar.Shield page does not contain the Retry member.')
}

if ($errors.Count -gt 0)
{
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

Write-Host "Verified $($uids.Count) API UIDs, $($publicTypes.Count) public runtime types, and generated API links."
