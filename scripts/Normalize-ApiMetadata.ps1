[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$MetadataPath
)

$ErrorActionPreference = 'Stop'
$resolvedMetadataPath = (Resolve-Path -LiteralPath $MetadataPath).Path
$metadataFiles = @(Get-ChildItem -LiteralPath $resolvedMetadataPath -Filter '*.yml' -File)
$localReferencePages = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
$localPages = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

function ConvertTo-ApiAnchor([string]$uid)
{
    return [regex]::Replace($uid, '[^A-Za-z0-9]', '_')
}

foreach ($metadataFile in $metadataFiles)
{
    if (([IO.File]::ReadLines($metadataFile.FullName) | Select-Object -First 1) -ne
        '### YamlMime:ManagedReference')
    {
        continue
    }

    $page = [IO.Path]::ChangeExtension($metadataFile.Name, '.html')
    [void]$localPages.Add($page)
    $isFirstItem = $true

    foreach ($line in [IO.File]::ReadLines($metadataFile.FullName))
    {
        if ($line -eq 'references:')
        {
            break
        }

        if ($line -match '^- uid:\s+(?<uid>\S+)')
        {
            $uid = $Matches.uid
            $isPrimaryItem = $isFirstItem
            $target = if ($isPrimaryItem)
            {
                $page
            }
            else
            {
                "$page#$(ConvertTo-ApiAnchor $uid)"
            }
            $localReferencePages[$uid] = $target
            $isFirstItem = $false

            if ($uid -match '^(?<overload>.+)\(')
            {
                [void]$localReferencePages.TryAdd("$($Matches.overload)*", $target)
            }
            elseif (-not $isPrimaryItem)
            {
                [void]$localReferencePages.TryAdd("$uid*", $target)
            }
        }
    }
}

foreach ($metadataFile in $metadataFiles)
{
    $content = [IO.File]::ReadAllText($metadataFile.FullName)
    $lines = $content -split '\r?\n'
    $normalizedLines = [Collections.Generic.List[string]]::new($lines.Count)
    $inReferences = $false
    $isLocalReference = $false
    $referenceUid = $null

    foreach ($line in $lines)
    {
        if ($line -eq 'references:')
        {
            $inReferences = $true
        }
        elseif ($inReferences -and $line -match '^\s*-\s*uid:\s+(?<uid>\S+)')
        {
            $referenceUid = $Matches.uid
            $isLocalReference = $referenceUid -eq 'Kevlar' -or
                $referenceUid.StartsWith('Kevlar.', [StringComparison]::Ordinal)
        }
        elseif ($inReferences -and $line -match '^\s*definition:\s+(?<uid>\S+)')
        {
            $referenceUid = $Matches.uid
            $isLocalReference = $referenceUid -eq 'Kevlar' -or
                $referenceUid.StartsWith('Kevlar.', [StringComparison]::Ordinal)
        }

        if ($line -match '^[ \t]*href:\s+(?<href>\S+)')
        {
            $isGitHubSourceLink = $Matches.href.StartsWith(
                'https://github.com/',
                [StringComparison]::OrdinalIgnoreCase)
            $isExternalLocalReference = $inReferences -and
                $isLocalReference -and
                $line.StartsWith('  href: https://learn.microsoft.com/', [StringComparison]::OrdinalIgnoreCase)
            $relativeTarget = $Matches.href.Split('#')[0]
            $isUnresolvedLocalReference = $inReferences -and
                $isLocalReference -and
                $relativeTarget.EndsWith('.html', [StringComparison]::OrdinalIgnoreCase) -and
                -not [Uri]::IsWellFormedUriString($relativeTarget, [UriKind]::Absolute) -and
                -not $localPages.Contains($relativeTarget)

            if ($isExternalLocalReference -or $isGitHubSourceLink -or $isUnresolvedLocalReference)
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

    $hrefInsertions = [Collections.Generic.Dictionary[int, string]]::new()
    $referenceStart = $normalizedLines.IndexOf('references:')

    if ($referenceStart -ge 0)
    {
        for ($i = $referenceStart + 1; $i -lt $normalizedLines.Count; $i++)
        {
            if ($normalizedLines[$i] -notmatch '^(?<indent>\s*)-\s*uid:\s+(?<uid>\S+)')
            {
                continue
            }

            $indent = $Matches.indent
            $uid = $Matches.uid
            if ($uid -ne 'Kevlar' -and
                -not $uid.StartsWith('Kevlar.', [StringComparison]::Ordinal))
            {
                continue
            }

            $fieldIndent = "$indent  "
            $entryEnd = $i + 1
            while ($entryEnd -lt $normalizedLines.Count -and
                -not $normalizedLines[$entryEnd].StartsWith("$indent- ", [StringComparison]::Ordinal))
            {
                $entryEnd++
            }

            $definition = $null
            $definitionIndex = -1
            $hrefIndex = -1
            $isExternalIndex = -1
            $nameIndex = -1
            for ($j = $i + 1; $j -lt $entryEnd; $j++)
            {
                if ($normalizedLines[$j] -match "^$([regex]::Escape($fieldIndent))definition:\s+(?<uid>\S+)")
                {
                    $definition = $Matches.uid
                    $definitionIndex = $j
                }
                elseif ($normalizedLines[$j].StartsWith("${fieldIndent}href: ", [StringComparison]::Ordinal))
                {
                    $hrefIndex = $j
                }
                elseif ($normalizedLines[$j].StartsWith("${fieldIndent}isExternal: ", [StringComparison]::Ordinal))
                {
                    $isExternalIndex = $j
                }
                elseif ($nameIndex -lt 0 -and
                    $normalizedLines[$j].StartsWith("${fieldIndent}name: ", [StringComparison]::Ordinal))
                {
                    $nameIndex = $j
                }
            }

            $target = $null
            $lookupUid = $definition ?? $uid
            if (-not $localReferencePages.TryGetValue($lookupUid, [ref]$target))
            {
                continue
            }

            if ($hrefIndex -ge 0)
            {
                continue
            }

            $insertionIndex = if ($isExternalIndex -ge 0)
            {
                $isExternalIndex + 1
            }
            elseif ($definition)
            {
                $definitionIndex + 1
            }
            elseif ($nameIndex -ge 0)
            {
                $nameIndex
            }
            else
            {
                $i + 1
            }

            $hrefInsertions[$insertionIndex] = "${fieldIndent}href: $target"
        }
    }

    $finalLines = [Collections.Generic.List[string]]::new($normalizedLines.Count + $hrefInsertions.Count)
    for ($i = 0; $i -lt $normalizedLines.Count; $i++)
    {
        $insertedHref = $null
        if ($hrefInsertions.TryGetValue($i, [ref]$insertedHref))
        {
            $finalLines.Add($insertedHref)
        }

        $finalLines.Add($normalizedLines[$i])
    }

    $normalized = $finalLines -join "`n"
    if ($normalized -ne $content)
    {
        [IO.File]::WriteAllText(
            $metadataFile.FullName,
            $normalized,
            [Text.UTF8Encoding]::new($false))
    }
}
