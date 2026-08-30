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

if ($sidebarSource -notmatch "'getting-started',\s*'polly-migration'")
{
    $errors.Add("Documentation sidebar must place 'polly-migration' after 'getting-started'.")
}
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

$deadApiPatterns = @(
    'FallbackWithNotifications'
    '\bonFallback\s*:'
    '\bWhenDefault\('
    '\bOrDefault\('
    '\bOrWhen\('
    'WhenResultDefault'
    'OrResultDefault'
    '\bHedgingOptions\b'
    '\bHedgingStrategyDescriptor\b'
    '\bStrategyKind\.Hedging\b'
    '\bStandardHedgingShieldOptions\b'
    '\bAddStandardHedgingShield\b'
    '\bVoidShield\b'
    '\bVoidShieldBuilder\b'
    '\bPartitionedVoidShield\b'
    '\bmaxQueue\b'
    '\bMaxQueue\b'
    '\bKevlar\.Extensions\.DependencyInjection\.BackoffKind\b'
    '\bRetryEvent\.Attempt\b'
    '\bRetryEvent<[^>]+>\.Attempt\b'
    '\bRetryNumber\b'
    '\bHedgeEvent\.Attempt\b'
    '\bRetryForever\s*\(\s*backoff\s*:\s*null\s*\)'
    '\bjitter\s*:\s*(?:true|false)\b'
    '\bJitter\s*=\s*(?:true|false)\b'
    '(?i:\.RateLimit\s*\(\s*(?:[A-Za-z_][A-Za-z0-9_]*)?(?:limiter|acquireLease)\s*(?:,|\)))'
    '\bRateLimiterRejectedEvent\b'
)
$deadApiPattern = [regex]($deadApiPatterns -join '|')
foreach ($document in @((Get-Item -LiteralPath (Join-Path $repositoryRoot 'README.md'))) + $documents)
{
    $documentLines = @(Get-Content -LiteralPath $document.FullName)
    $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $document.FullName).Replace('\', '/')
    for ($lineIndex = 0; $lineIndex -lt $documentLines.Count; $lineIndex++)
    {
        $line = $documentLines[$lineIndex]
        if ($deadApiPattern.IsMatch($line))
        {
            $errors.Add("$relativePath`:$($lineIndex + 1) contains a dead pre-release API name.")
        }
    }
}

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

$targetFrameworkClaims = @(
    @{
        Document = 'docs/docs/getting-started.md'
        Project = 'src/Kevlar/Kevlar.csproj'
        ClaimPattern = [regex]'(?m)^The core targets .+$'
    }
    @{
        Document = 'docs/docs/intro.md'
        Project = 'src/Kevlar/Kevlar.csproj'
        ClaimPattern = [regex]'(?m)^- \*\*Broad reach\.\*\* .+$'
    }
    @{
        Document = 'docs/docs/grpc.md'
        Project = 'src/Kevlar.Extensions.Grpc/Kevlar.Extensions.Grpc.csproj'
        ClaimPattern = [regex]'(?m)^The interceptor .+ targets .+$'
    }
)
$targetFrameworkPattern = [regex]'`(?<tfm>net(?:standard)?\d+\.\d+)`'

foreach ($claim in $targetFrameworkClaims)
{
    $projectSource = Get-Content -LiteralPath (Join-Path $repositoryRoot $claim.Project) -Raw
    $projectFrameworksMatch = [regex]::Match(
        $projectSource,
        '<TargetFrameworks>(?<frameworks>[^<]+)</TargetFrameworks>')
    if (-not $projectFrameworksMatch.Success)
    {
        $errors.Add("$($claim.Project) must declare TargetFrameworks for documentation validation.")
        continue
    }

    $expectedFrameworks = @($projectFrameworksMatch.Groups['frameworks'].Value -split ';')
    $documentSource = Get-Content -LiteralPath (Join-Path $repositoryRoot $claim.Document) -Raw
    $claimMatch = $claim.ClaimPattern.Match($documentSource)
    if (-not $claimMatch.Success)
    {
        $errors.Add("$($claim.Document) must contain its package target-framework claim.")
        continue
    }

    $documentFrameworks = @(
        $targetFrameworkPattern.Matches($claimMatch.Value) |
            ForEach-Object { $_.Groups['tfm'].Value } |
            Select-Object -Unique
    )
    if (($documentFrameworks -join ';') -cne ($expectedFrameworks -join ';'))
    {
        $errors.Add(
            "$($claim.Document) target frameworks '$($documentFrameworks -join ';')' do not match " +
            "$($claim.Project) '$($expectedFrameworks -join ';')'.")
    }
}

$requiredFirstFenceUsings = [ordered]@{
    'dependency-injection.md' = 'using Microsoft.Extensions.DependencyInjection;'
    'http.md' = 'using Microsoft.Extensions.DependencyInjection;'
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

$forbiddenRegistrationUsings = [ordered]@{
    'dependency-injection.md' = 'using Kevlar.Extensions.DependencyInjection;'
    'http.md' = 'using Kevlar.Extensions.Http;'
}
foreach ($entry in $forbiddenRegistrationUsings.GetEnumerator())
{
    $documentPath = Join-Path $resolvedDocsPath $entry.Key
    $firstFence = Get-FirstCsharpFence @(Get-Content -LiteralPath $documentPath)
    if ($null -ne $firstFence -and $firstFence.Body -contains $entry.Value)
    {
        $errors.Add("$($entry.Key)'s first C# fence must not contain registration-only import '$($entry.Value)'.")
    }
}

$dependencyInjectionSource = Get-Content -LiteralPath (Join-Path $resolvedDocsPath 'dependency-injection.md') -Raw
if ($dependencyInjectionSource.Contains('builder.Configuration', [StringComparison]::Ordinal))
{
    $errors.Add('dependency-injection.md must define IConfiguration explicitly instead of relying on an undefined builder.Configuration.')
}

foreach ($requiredPage in @('glossary.md', 'cookbook.md'))
{
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedDocsPath $requiredPage)))
    {
        $errors.Add("Required documentation page '$requiredPage' is missing.")
    }
}

$webApiProgram = Get-Content -LiteralPath (Join-Path $repositoryRoot 'samples/WebApi/Program.cs') -Raw
if (-not $webApiProgram.Contains('.MapGet(', [StringComparison]::Ordinal))
{
    $errors.Add('samples/WebApi must map a minimal API endpoint with MapGet.')
}

foreach ($sampleDirectory in Get-ChildItem (Join-Path $repositoryRoot 'samples') -Directory)
{
    $sampleReadme = Join-Path $sampleDirectory.FullName 'README.md'
    if (-not (Test-Path -LiteralPath $sampleReadme))
    {
        continue
    }

    $wordCount = @((Get-Content -LiteralPath $sampleReadme -Raw) -split '\s+' | Where-Object { $_ }).Count
    if ($wordCount -lt 70)
    {
        $errors.Add("Sample README '$($sampleDirectory.Name)' must contain at least 70 words; found $wordCount.")
    }
}

$benchmarkDescriptions = [ordered]@{
    'OverheadBenchmarks.cs' = 'Kevlar_EmptyOutcomeState'
    'TimeoutBenchmarks.cs' = 'Kevlar_SynchronousGenerator_HappyPath'
    'ConcurrencyLimitBenchmarks.cs' = 'Kevlar_WithHooks_Uncontended'
    'RateLimitBenchmarks.cs' = 'Kevlar_WithHooks_Uncontended'
    'FallbackBenchmarks.cs' = 'Kevlar_NoNotification'
    'PipelineBenchmarks.cs' = 'Kevlar_TokenBucketRatioFiveStrategyChainSync'
}
foreach ($entry in $benchmarkDescriptions.GetEnumerator())
{
    $benchmarkPath = Join-Path $repositoryRoot "benchmarks/Kevlar.Benchmarks/$($entry.Key)"
    $benchmarkSource = Get-Content -LiteralPath $benchmarkPath -Raw
    $pattern = '(?s)\[[^\]]*Benchmark\([^\]]*Description\s*=\s*"[^"]+"[^\]]*\)\]\s*public\s+[^\r\n]+\s+{0}\s*\(' -f [regex]::Escape($entry.Value)
    if (-not [regex]::IsMatch($benchmarkSource, $pattern))
    {
        $errors.Add("Benchmark '$($entry.Value)' in '$($entry.Key)' must declare Benchmark.Description.")
    }
}

$grpcSource = Get-Content -LiteralPath (Join-Path $resolvedDocsPath 'grpc.md') -Raw
if ($grpcSource.Contains('doc-test-ignore', [StringComparison]::Ordinal))
{
    $errors.Add('grpc.md snippets must compile against the documentation test client instead of using doc-test-ignore.')
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

$contributorOnlyPatterns = [ordered]@{
    'Stryker' = [regex]'(?i)\bStryker\b'
    'mutation score' = [regex]'(?i)\bmutation score\b'
    'coverage.runsettings' = [regex]'(?i)\bcoverage\.runsettings\b'
    'Verify-PublishCompatibility' = [regex]'(?i)\bVerify-PublishCompatibility\b'
}

foreach ($document in $documents)
{
    $relativePath = [IO.Path]::GetRelativePath($resolvedDocsPath, $document.FullName).Replace('\', '/')
    $lines = @(Get-Content -LiteralPath $document.FullName)
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++)
    {
        foreach ($entry in $contributorOnlyPatterns.GetEnumerator())
        {
            if ($entry.Value.IsMatch($lines[$lineIndex]))
            {
                $errors.Add(
                    "Contributor-only term '$($entry.Key)' at ${relativePath}:$($lineIndex + 1). " +
                    'Move repository maintenance guidance to CONTRIBUTING.md.')
            }
        }
    }
}

$testingDocument = $visibleDocuments | Where-Object Path -eq 'testing.md' | Select-Object -First 1
$requiredTestingHeadings = @(
    '## Deterministic time with TimeProvider'
    '## Asserting pipeline shape'
    '## Recording telemetry'
    '## Testing HTTP shields'
    '## Testing partitioned shields'
    '## Chaos in tests'
)

if ($null -eq $testingDocument)
{
    $errors.Add("Documentation page 'testing.md' is missing.")
}
else
{
    foreach ($heading in $requiredTestingHeadings)
    {
        if ($heading -notin $testingDocument.Lines)
        {
            $errors.Add("Documentation page 'testing.md' must contain heading '$heading'.")
        }
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
$analyzerDiagnosticDirectivePattern = [regex]'^\s*<!--\s*doc-test-diagnostic:\s*[^>]+-->\s*$'
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
            $withoutDiagnosticDirective = $analyzerDiagnosticDirectivePattern.Replace($withoutAnalyzerLinks, '')
            if ($analyzerMentionPattern.IsMatch($withoutDiagnosticDirective))
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

& (Join-Path $PSScriptRoot 'Verify-Samples.ps1')
if ($LASTEXITCODE -ne 0)
{
    throw "Sample verification failed with exit code $LASTEXITCODE."
}

Write-Host "Verified $($visibleDocuments.Count) documentation pages: structure, canonical analyzer rules, benchmark claims, and samples are valid."
