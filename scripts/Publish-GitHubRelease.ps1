[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Tag,

    [Parameter(Mandatory)]
    [string]$NotesPath,

    [Parameter(Mandatory)]
    [string]$AssetsPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedNotesPath = (Resolve-Path -LiteralPath $NotesPath).Path
$resolvedAssetsPath = (Resolve-Path -LiteralPath $AssetsPath).Path
$assets = @(Get-ChildItem -LiteralPath $resolvedAssetsPath -File |
    Where-Object Extension -in '.nupkg', '.snupkg' |
    Sort-Object Name |
    ForEach-Object FullName)
if ($assets.Count -eq 0)
{
    throw "No release assets were found in '$resolvedAssetsPath'."
}

& gh release view $Tag *> $null
if ($LASTEXITCODE -eq 0)
{
    & gh release edit $Tag --notes-file $resolvedNotesPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Updating GitHub release '$Tag' failed."
    }

    & gh release upload $Tag @assets --clobber
    if ($LASTEXITCODE -ne 0)
    {
        throw "Uploading assets to GitHub release '$Tag' failed."
    }

    return
}

& gh release create $Tag --verify-tag --notes-file $resolvedNotesPath @assets
if ($LASTEXITCODE -ne 0)
{
    throw "Creating GitHub release '$Tag' failed."
}
