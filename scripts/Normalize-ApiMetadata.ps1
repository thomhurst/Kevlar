[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$MetadataPath
)

$ErrorActionPreference = 'Stop'
$resolvedMetadataPath = (Resolve-Path -LiteralPath $MetadataPath).Path

foreach ($metadataFile in Get-ChildItem -LiteralPath $resolvedMetadataPath -Filter '*.yml' -File)
{
    $content = [IO.File]::ReadAllText($metadataFile.FullName)
    $lines = $content -split '\r?\n'
    $normalizedLines = [Collections.Generic.List[string]]::new($lines.Count)
    $inReferences = $false
    $isLocalReference = $false

    foreach ($line in $lines)
    {
        if ($line -eq 'references:')
        {
            $inReferences = $true
        }
        elseif ($inReferences -and $line -match '^- uid:\s+(?<uid>\S+)')
        {
            $isLocalReference = $Matches.uid -eq 'Kevlar' -or
                $Matches.uid.StartsWith('Kevlar.', [StringComparison]::Ordinal)
        }

        if ($line -match '^[ \t]*href:\s+(?<href>\S+)')
        {
            $isGitHubSourceLink = $Matches.href.StartsWith(
                'https://github.com/',
                [StringComparison]::OrdinalIgnoreCase)
            $isExternalLocalReference = $inReferences -and
                $isLocalReference -and
                $line.StartsWith('  href: https://learn.microsoft.com/', [StringComparison]::OrdinalIgnoreCase)

            if ($isExternalLocalReference -or $isGitHubSourceLink)
            {
                continue
            }
        }

        $normalizedLine = $line
        if ($line -match '^[ \t]*commentId:')
        {
            $normalizedLine = $line `
                -replace '(?<=\{)``(?=\d+\})', '`' `
                -replace '^(\s*commentId:\s+T:Kevlar\.Shield\{)`0\}$', '${1}`1}'
        }

        $normalizedLines.Add($normalizedLine)
    }

    $normalized = $normalizedLines -join "`n"
    if ($normalized -ne $content)
    {
        [IO.File]::WriteAllText(
            $metadataFile.FullName,
            $normalized,
            [Text.UTF8Encoding]::new($false))
    }
}
