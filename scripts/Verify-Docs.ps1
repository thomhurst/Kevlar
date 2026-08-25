[CmdletBinding()]
param(
    [string]$DocsPath,
    [string]$SidebarPath
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
if (-not $DocsPath)
{
    $DocsPath = Join-Path $repositoryRoot 'docs/docs'
}

if (-not $SidebarPath)
{
    $SidebarPath = Join-Path $repositoryRoot 'docs/sidebars.ts'
}

$resolvedDocsPath = (Resolve-Path -LiteralPath $DocsPath).Path
$resolvedSidebarPath = (Resolve-Path -LiteralPath $SidebarPath).Path
$sidebarSource = Get-Content -LiteralPath $resolvedSidebarPath -Raw
$sidebarIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$sidebarEntryPattern = [regex]"'(?<id>[^']+)'"

foreach ($sidebarMatch in $sidebarEntryPattern.Matches($sidebarSource))
{
    [void]$sidebarIds.Add($sidebarMatch.Groups['id'].Value)
}

$documents = Get-ChildItem -LiteralPath $resolvedDocsPath -Recurse -File |
    Where-Object Extension -in '.md', '.mdx' |
    Sort-Object FullName
$visibleDocuments = [System.Collections.Generic.List[object]]::new()
$errors = [System.Collections.Generic.List[string]]::new()
$timeoutExceptionPattern = [regex]'(?:(?:When|Or)(?:<|&lt;)(?:(?:global::)?System\.)?TimeoutException(?:>|&gt;)|\bis\s+(?:(?:global::)?System\.)?TimeoutException\b|\bcatch\s*\(\s*(?:(?:global::)?System\.)?TimeoutException\b)'
$timeoutExceptionAllowMarker = '<!-- doc-lint: allow-TimeoutException -->'

function Get-FirstCsharpFence([string[]]$Lines)
{
    for ($lineIndex = 0; $lineIndex -lt $Lines.Count; $lineIndex++)
    {
        if ($Lines[$lineIndex] -notmatch '^```csharp\s*$')
        {
            continue
        }

        $fenceLine = $lineIndex + 1
        $body = [System.Collections.Generic.List[string]]::new()
        for ($lineIndex++; $lineIndex -lt $Lines.Count -and $Lines[$lineIndex] -notmatch '^```\s*$'; $lineIndex++)
        {
            $body.Add($Lines[$lineIndex])
        }

        return [pscustomobject]@{ Line = $fenceLine; Body = $body.ToArray() }
    }

    return $null
}

function Get-FirstMultiStrategyFenceLine([string[]]$Lines)
{
    $strategyPattern = [regex]'\.(?:Timeout|Retry|CircuitBreaker|RateLimit|ConcurrencyLimit|Hedge|Fallback)\('
    for ($lineIndex = 0; $lineIndex -lt $Lines.Count; $lineIndex++)
    {
        if ($Lines[$lineIndex] -notmatch '^```csharp\s*$')
        {
            continue
        }

        $fenceLine = $lineIndex + 1
        $strategyCount = 0
        for ($lineIndex++; $lineIndex -lt $Lines.Count -and $Lines[$lineIndex] -notmatch '^```\s*$'; $lineIndex++)
        {
            $strategyCount += $strategyPattern.Matches($Lines[$lineIndex]).Count
        }

        if ($strategyCount -ge 2)
        {
            return $fenceLine
        }
    }

    return -1
}

$lintDocuments = @(
    Get-Item -LiteralPath (Join-Path $repositoryRoot 'README.md')
    $documents
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs/src') -Recurse -File -Filter '*.tsx'
)

foreach ($lintDocument in $lintDocuments)
{
    $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $lintDocument.FullName).Replace('\', '/')
    $lines = @(Get-Content -LiteralPath $lintDocument.FullName)
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++)
    {
        $line = $lines[$lineIndex]
        if ($timeoutExceptionPattern.IsMatch($line) -and -not $line.Contains($timeoutExceptionAllowMarker, [StringComparison]::Ordinal))
        {
            $location = "${relativePath}:$($lineIndex + 1)"
            $errors.Add(
                "Forbidden System.TimeoutException handling example at $location. " +
                "Use TimeoutExceededException or add '$timeoutExceptionAllowMarker' when demonstrating the trap.")
        }
    }
}

$onboardingPaths = @(
    (Join-Path $repositoryRoot 'README.md')
    (Join-Path $resolvedDocsPath 'getting-started.md')
)
foreach ($onboardingPath in $onboardingPaths)
{
    $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $onboardingPath).Replace('\', '/')
    $onboardingLines = @(Get-Content -LiteralPath $onboardingPath)
    $source = $onboardingLines -join "`n"
    if ($source -notmatch 'Retry\(3' -or $source -notmatch '4 total attempts')
    {
        $errors.Add("$relativePath must explain that Retry(3) allows 4 total attempts.")
    }

    $outermostLine = -1
    for ($lineIndex = 0; $lineIndex -lt $onboardingLines.Count; $lineIndex++)
    {
        if ($onboardingLines[$lineIndex] -match 'first strategy is the')
        {
            $outermostLine = $lineIndex + 1
            break
        }
    }

    $multiStrategyFenceLine = Get-FirstMultiStrategyFenceLine $onboardingLines
    if ($multiStrategyFenceLine -lt 0 -or $outermostLine -lt 0 -or $outermostLine -ge $multiStrategyFenceLine)
    {
        $errors.Add("$relativePath must state that the first strategy is outermost before its first multi-strategy example.")
    }
}

foreach ($relativePath in @('docs/docs/getting-started.md', 'docs/docs/intro.md'))
{
    $source = Get-Content -LiteralPath (Join-Path $repositoryRoot $relativePath) -Raw
    if ($source -match '(?m)^\|\s*Package\s*\|')
    {
        $errors.Add("$relativePath must link to the canonical README package table instead of duplicating it.")
    }
    if (-not $source.Contains('https://github.com/thomhurst/Kevlar#packages', [StringComparison]::Ordinal))
    {
        $errors.Add("$relativePath must link to the canonical README package table.")
    }
}

$requiredFirstFenceUsings = [ordered]@{
    'dependency-injection.md' = 'using Kevlar.Extensions.DependencyInjection;'
    'http.md' = 'using Kevlar.Extensions.Http;'
    'chaos.md' = 'using Kevlar.Chaos;'
    'observability.md' = 'using Kevlar;'
}
foreach ($entry in $requiredFirstFenceUsings.GetEnumerator())
{
    $documentPath = Join-Path $resolvedDocsPath $entry.Key
    $firstFence = Get-FirstCsharpFence @(Get-Content -LiteralPath $documentPath)
    if ($null -eq $firstFence -or $firstFence.Body -notcontains $entry.Value)
    {
        $errors.Add("$($entry.Key)'s first C# fence must contain '$($entry.Value)'.")
    }
}

$dependencyInjectionSource = Get-Content -LiteralPath (Join-Path $resolvedDocsPath 'dependency-injection.md') -Raw
if ($dependencyInjectionSource.Contains('builder.Configuration', [StringComparison]::Ordinal))
{
    $errors.Add('dependency-injection.md must define IConfiguration explicitly instead of relying on an undefined builder.Configuration.')
}

foreach ($document in $documents)
{
    $relativePath = [IO.Path]::GetRelativePath($resolvedDocsPath, $document.FullName).Replace('\', '/')
    $documentId = $relativePath.Substring(0, $relativePath.Length - $document.Extension.Length)
    $lines = @(Get-Content -LiteralPath $document.FullName)
    $frontMatterEnd = -1

    if ($lines.Count -gt 0 -and $lines[0].Trim() -eq '---')
    {
        for ($lineIndex = 1; $lineIndex -lt $lines.Count; $lineIndex++)
        {
            if ($lines[$lineIndex].Trim() -eq '---')
            {
                $frontMatterEnd = $lineIndex
                break
            }
        }
    }

    $frontMatter = if ($frontMatterEnd -gt 1) { $lines[1..($frontMatterEnd - 1)] } else { @() }
    $hidden = $frontMatter | Where-Object { $_ -match '^\s*(?:draft|unlisted):\s*true\s*$' }
    if ($hidden)
    {
        continue
    }

    $sidebarPosition = $null
    foreach ($line in $frontMatter)
    {
        if ($line -match '^\s*sidebar_position:\s*(\d+)\s*$')
        {
            $sidebarPosition = [int]$Matches[1]
            break
        }
    }

    $visibleDocuments.Add([pscustomobject]@{
        Id = $documentId
        Path = $relativePath
        Directory = [IO.Path]::GetDirectoryName($relativePath).Replace('\', '/')
        SidebarPosition = $sidebarPosition
        Lines = $lines
    })

    if (-not $sidebarIds.Contains($documentId))
    {
        $errors.Add("Orphaned document '$relativePath' is not listed in docs/sidebars.ts.")
    }
}

$positionedDocuments = $visibleDocuments | Where-Object { $null -ne $_.SidebarPosition }
$duplicatePositions = $positionedDocuments |
    Group-Object Directory, SidebarPosition |
    Where-Object Count -gt 1

foreach ($duplicate in $duplicatePositions)
{
    $paths = ($duplicate.Group.Path | Sort-Object) -join ', '
    $errors.Add("Duplicate sidebar_position $($duplicate.Group[0].SidebarPosition) in '$($duplicate.Group[0].Directory)': $paths.")
}

$partitionLinkPattern = [regex]'\[[^\]]*(?:AddPartitionedShield|PartitionedVoidShield|PartitionedShield)[^\]]*\]\((?:\.\./)*partitioning\.md(?:#[^)]+)?\)'
$partitionMentionPattern = [regex]'\b(?:AddPartitionedShield|PartitionedVoidShield|PartitionedShield)\b'
$analyzerLinkPattern = [regex]'\[[^\]]*KEV\d{3}[^\]]*\]\((?:\.\./)*analyzers\.md(?:#[^)]+)?\)'
$analyzerMentionPattern = [regex]'\bKEV\d{3}\b'
$hardwareMentionPattern = [regex]'(?i)(?:\b(?:AMD\s+)?(?:Ryzen|EPYC)\b|\bIntel\s+(?:Core(?:\s+Ultra)?|Xeon)\b|\bApple\s+(?:silicon|M\d)\b|\b(?:Qualcomm\s+)?Snapdragon\b|\b(?:AWS\s+)?Graviton\d*\b|\bARM\s+Neoverse\b|\bi[3579](?:-\s*|\s+)?\d{4,5}[A-Z]*\b)'

foreach ($document in $visibleDocuments | Where-Object Path -ne 'partitioning.md')
{
    $insideFence = $false

    for ($lineIndex = 0; $lineIndex -lt $document.Lines.Count; $lineIndex++)
    {
        $line = $document.Lines[$lineIndex]
        if ($line -match '^\s*```')
        {
            $insideFence = -not $insideFence
            continue
        }

        if ($insideFence)
        {
            continue
        }

        $withoutValidLinks = $partitionLinkPattern.Replace($line, '')
        if ($partitionMentionPattern.IsMatch($withoutValidLinks))
        {
            $errors.Add("Unlinked partitioning API mention at $($document.Path):$($lineIndex + 1).")
        }
    }
}

foreach ($document in $visibleDocuments)
{
    for ($lineIndex = 0; $lineIndex -lt $document.Lines.Count; $lineIndex++)
    {
        $line = $document.Lines[$lineIndex]
        if ($document.Path -ne 'analyzers.md')
        {
            $withoutAnalyzerLinks = $analyzerLinkPattern.Replace($line, '')
            if ($analyzerMentionPattern.IsMatch($withoutAnalyzerLinks))
            {
                $errors.Add("Analyzer rule duplicated outside analyzers.md at $($document.Path):$($lineIndex + 1).")
            }
        }

        if ($document.Path -ne 'benchmarks.md' -and $hardwareMentionPattern.IsMatch($line))
        {
            $errors.Add("Hardware-specific benchmark claim outside benchmarks.md at $($document.Path):$($lineIndex + 1).")
        }
    }
}

$exceptionReferencePages = @(
    $visibleDocuments | Where-Object Directory -eq 'strategies'
    $visibleDocuments | Where-Object Path -in @(
        'getting-started.md',
        'handling-failures.md',
        'polly-migration.md')
)

foreach ($document in $exceptionReferencePages)
{
    $expectedTarget = if ($document.Directory -eq 'strategies')
    {
        '../exceptions.md'
    }
    else
    {
        'exceptions.md'
    }

    $linkPattern = [regex]::Escape("($expectedTarget)")
    if (($document.Lines -join "`n") -notmatch $linkPattern)
    {
        $errors.Add("Documentation page '$($document.Path)' must link to '$expectedTarget'.")
    }
}

if ($errors.Count -gt 0)
{
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

Write-Host "Verified $($visibleDocuments.Count) documentation pages: structure, canonical analyzer rules, and benchmark claims are valid."
