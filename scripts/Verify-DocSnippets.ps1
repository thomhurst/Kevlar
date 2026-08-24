[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagesPath,

    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$resolvedPackagesPath = (Resolve-Path -LiteralPath $PackagesPath).Path
$generatedDirectory = Join-Path $repositoryRoot 'artifacts/doc-tests/generated'
$generatedPath = Join-Path $generatedDirectory 'GeneratedSnippets.g.cs'
$nugetConfigPath = Join-Path $generatedDirectory 'NuGet.config'
$projectPath = Join-Path $repositoryRoot 'tests/Kevlar.DocTests/Kevlar.DocTests.csproj'
$documentPaths = @(
    (Join-Path $repositoryRoot 'README.md')
    Get-ChildItem (Join-Path $repositoryRoot 'docs/docs') -Recurse -File -Include '*.md', '*.mdx' |
        Sort-Object FullName |
        ForEach-Object FullName
)

$requiredPackages = @(
    'Kevlar'
    'Kevlar.Analyzers'
    'Kevlar.Chaos'
    'Kevlar.Extensions.DependencyInjection'
    'Kevlar.Extensions.Grpc'
    'Kevlar.Extensions.Http'
    'Kevlar.Extensions.RateLimiting'
    'Kevlar.Testing'
)

foreach ($packageId in $requiredPackages)
{
    $packagePath = Join-Path $resolvedPackagesPath "$packageId.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf))
    {
        throw "Documented package '$packageId' was not packed at '$packagePath'."
    }
}

$snippets = [System.Collections.Generic.List[object]]::new()
$installPackageIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$projectCommands = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($documentPath in $documentPaths)
{
    $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $documentPath).Replace('\', '/')
    $lines = Get-Content -LiteralPath $documentPath
    $csharpOrdinal = 0

    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++)
    {
        if ($lines[$lineIndex] -match '^```csharp\s*$')
        {
            $csharpOrdinal++
            $startLine = $lineIndex + 2
            $body = [System.Collections.Generic.List[string]]::new()
            for ($lineIndex++; $lineIndex -lt $lines.Count -and $lines[$lineIndex] -notmatch '^```\s*$'; $lineIndex++)
            {
                $body.Add($lines[$lineIndex])
            }

            if ($lineIndex -ge $lines.Count)
            {
                throw "Unclosed C# fence at ${relativePath}:$startLine."
            }

            $directive = if ($startLine -ge 3) { $lines[$startLine - 3].Trim() } else { '' }
            $ignoreReason = $null
            if ($directive -match '^<!--\s*doc-test-ignore:\s*(.+?)\s*-->$')
            {
                $ignoreReason = $Matches[1]
            }

            $ignored = $null -ne $ignoreReason
            if ($directive -match '^<!--\s*doc-test-ignore' -and -not $ignored)
            {
                throw "Malformed doc-test-ignore directive before ${relativePath}:$startLine. Include a non-empty reason."
            }

            $mode = 'statements'
            $splitBefore = $null
            $runName = $null
            if ($directive -match '^<!--\s*doc-test-run:\s*([a-z0-9-]+)\s*-->$')
            {
                $runName = $Matches[1]
            }

            if ($directive -match '^<!--\s*doc-test-declaration:\s*split-before=(.+?)\s*-->$')
            {
                $mode = 'declaration'
                $splitBefore = $Matches[1]
            }
            elseif ($directive -match '^<!--\s*doc-test-tail-declaration:\s*split-before=(.+?)\s*-->$')
            {
                $mode = 'tail-declaration'
                $splitBefore = $Matches[1]
            }
            elseif ($directive -match '^<!--\s*doc-test-declaration\s*-->$')
            {
                $mode = 'declaration'
            }
            elseif ($directive -match '^<!--\s*doc-test-strategy-member\s*-->$')
            {
                $mode = 'strategy-member'
            }
            elseif ($directive -match '^<!--\s*doc-test-' -and -not $ignored -and $null -eq $runName)
            {
                throw "Unknown or malformed doc-test directive before ${relativePath}:$startLine."
            }

            $source = $body -join "`n"
            if ($ignored)
            {
                Write-Host "EXCLUDED $relativePath#$csharpOrdinal ($ignoreReason)"
                continue
            }

            $sourceWithoutLineComments = [regex]::Replace($source, '(?s)/\*.*?\*/', '') -replace '(?m)//.*$', ''
            if ($sourceWithoutLineComments -match '\.\.\.')
            {
                throw "Incomplete C# at $relativePath#$csharpOrdinal must have a doc-test-ignore directive with a reason."
            }

            $snippets.Add([pscustomobject]@{
                Id = "$relativePath#$csharpOrdinal"
                Path = $relativePath
                SourcePath = $documentPath.Replace('\', '/')
                Line = $startLine
                Source = $source
                Mode = $mode
                SplitBefore = $splitBefore
                RunName = $runName
            })

            continue
        }

        if ($lines[$lineIndex] -match '^```(?:bash|sh|shell|powershell|pwsh)\s*$')
        {
            for ($lineIndex++; $lineIndex -lt $lines.Count -and $lines[$lineIndex] -notmatch '^```\s*$'; $lineIndex++)
            {
                $command = $lines[$lineIndex].Trim()
                if ($command -match '^dotnet add package\s+([A-Za-z0-9_.-]+)')
                {
                    [void]$installPackageIds.Add($Matches[1])
                }

                if ($command -match '^dotnet run\s+.*--project\s+([^\s]+)')
                {
                    [void]$projectCommands.Add($Matches[1].Trim("'`""))
                }
            }
        }
    }
}

foreach ($packageId in $installPackageIds)
{
    if ($packageId -notin $requiredPackages)
    {
        throw "Shell sample references unexpected package ID '$packageId'."
    }
}

foreach ($relativeProjectPath in $projectCommands)
{
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativeProjectPath)))
    {
        throw "Shell sample references missing project '$relativeProjectPath'."
    }
}

New-Item -ItemType Directory -Force -Path $generatedDirectory | Out-Null

$builder = [Text.StringBuilder]::new()
[void]$builder.AppendLine('// <auto-generated />')
[void]$builder.AppendLine('#nullable enable')
[void]$builder.AppendLine('#pragma warning disable CS0162 // Harness appends a fallback return after documentation control flow.')
[void]$builder.AppendLine('using System.Diagnostics.Metrics;')
[void]$builder.AppendLine('using System.Net;')
[void]$builder.AppendLine('using Kevlar;')
[void]$builder.AppendLine('using Kevlar.Chaos;')
[void]$builder.AppendLine('using Kevlar.Extensions.DependencyInjection;')
[void]$builder.AppendLine('using Kevlar.Extensions.Grpc;')
[void]$builder.AppendLine('using Kevlar.Extensions.Http;')
[void]$builder.AppendLine('using Kevlar.Extensions.RateLimiting;')
[void]$builder.AppendLine('using Kevlar.Testing;')
[void]$builder.AppendLine('using Microsoft.Extensions.DependencyInjection;')
[void]$builder.AppendLine('using Microsoft.Extensions.Configuration;')
[void]$builder.AppendLine('using Microsoft.Extensions.Time.Testing;')
[void]$builder.AppendLine('using Microsoft.Extensions.Logging;')
[void]$builder.AppendLine('using System.Threading.RateLimiting;')
[void]$builder.AppendLine('using OpenTelemetry.Metrics;')
[void]$builder.AppendLine('using Polly;')
[void]$builder.AppendLine('using Polly.CircuitBreaker;')
[void]$builder.AppendLine('using Polly.Retry;')
[void]$builder.AppendLine('using Polly.Timeout;')
[void]$builder.AppendLine('namespace Kevlar.DocTests;')

for ($snippetIndex = 0; $snippetIndex -lt $snippets.Count; $snippetIndex++)
{
    $snippet = $snippets[$snippetIndex]
    $source = $snippet.Source -replace '(?m)^using\s+[A-Za-z0-9_.]+;\s*(?://.*)?$', ''
    [void]$builder.AppendLine("internal sealed class Snippet$snippetIndex : SnippetContext")
    [void]$builder.AppendLine('{')

    if ($snippet.Mode -eq 'declaration')
    {
        if ($null -eq $snippet.SplitBefore)
        {
            $declarationSource = $source.TrimEnd()
            $source = ''
        }
        else
        {
            $splitIndex = $source.IndexOf($snippet.SplitBefore, [StringComparison]::Ordinal)
            if ($splitIndex -lt 0)
            {
                throw "Declaration split marker '$($snippet.SplitBefore)' was not found in $($snippet.Id)."
            }

            $declarationSource = $source.Substring(0, $splitIndex).TrimEnd()
            $source = $source.Substring($splitIndex)
        }
        [void]$builder.AppendLine("#line $($snippet.Line) `"$($snippet.SourcePath)`"")
        foreach ($line in ($declarationSource -split "`n"))
        {
            [void]$builder.AppendLine("    $line")
        }
        [void]$builder.AppendLine('#line default')
    }
    elseif ($snippet.Mode -eq 'strategy-member')
    {
        [void]$builder.AppendLine('    private sealed class DocumentationStrategy : Strategy')
        [void]$builder.AppendLine('    {')
        [void]$builder.AppendLine("#line $($snippet.Line) `"$($snippet.SourcePath)`"")
        foreach ($line in ($source -split "`n"))
        {
            [void]$builder.AppendLine("        $line")
        }
        [void]$builder.AppendLine('#line default')
        if ($source -notmatch '\bExecuteAsync\b')
        {
            [void]$builder.AppendLine('        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(')
            [void]$builder.AppendLine('            Continuation<T, TState> next, KevlarContext context) =>')
            [void]$builder.AppendLine('            next.InvokeAsync(context);')
        }
        [void]$builder.AppendLine('    }')
        $source = ''
    }
    elseif ($snippet.Mode -eq 'tail-declaration')
    {
        $splitIndex = $source.IndexOf($snippet.SplitBefore, [StringComparison]::Ordinal)
        if ($splitIndex -lt 0)
        {
            throw "Tail declaration split marker '$($snippet.SplitBefore)' was not found in $($snippet.Id)."
        }

        $declarationSource = $source.Substring($splitIndex).TrimEnd()
        $source = $source.Substring(0, $splitIndex).TrimEnd()
        [void]$builder.AppendLine("#line $($snippet.Line) `"$($snippet.SourcePath)`"")
        foreach ($line in ($declarationSource -split "`n"))
        {
            [void]$builder.AppendLine("    $line")
        }
        [void]$builder.AppendLine('#line default')
    }

    [void]$builder.AppendLine('    public static async Task<object?> RunAsync()')
    [void]$builder.AppendLine('    {')
    if (-not [string]::IsNullOrWhiteSpace($source))
    {
        [void]$builder.AppendLine("#line $($snippet.Line) `"$($snippet.SourcePath)`"")
        foreach ($line in ($source -split "`n"))
        {
            [void]$builder.AppendLine("        $line")
        }
        [void]$builder.AppendLine('#line default')
    }
    [void]$builder.AppendLine('        return null;')
    [void]$builder.AppendLine('    }')
    [void]$builder.AppendLine('}')
}

[void]$builder.AppendLine('internal static class SnippetCatalog')
[void]$builder.AppendLine('{')
[void]$builder.AppendLine('    public static async Task RunAsync()')
[void]$builder.AppendLine('    {')
[void]$builder.AppendLine("        Console.WriteLine(`"Compiled $($snippets.Count) C# documentation snippets.`");")
[void]$builder.AppendLine('        var output = new StringWriter();')
[void]$builder.AppendLine('        var original = Console.Out;')
[void]$builder.AppendLine('        Console.SetOut(output);')
[void]$builder.AppendLine('        try')
[void]$builder.AppendLine('        {')

$observableSnippetIndex = -1
$invalidCompositionSnippetIndex = -1
$timeoutExceededClauseSnippetIndex = -1
$systemTimeoutClauseTrapSnippetIndex = -1
$metricsSnippetIndex = -1
$retryNumbersSnippetIndex = -1
for ($snippetIndex = 0; $snippetIndex -lt $snippets.Count; $snippetIndex++)
{
    if ($snippets[$snippetIndex].RunName -eq 'pipeline-description')
    {
        $observableSnippetIndex = $snippetIndex
    }

    if ($snippets[$snippetIndex].RunName -eq 'invalid-composition')
    {
        $invalidCompositionSnippetIndex = $snippetIndex
    }

    if ($snippets[$snippetIndex].RunName -eq 'timeout-exceeded-clause')
    {
        $timeoutExceededClauseSnippetIndex = $snippetIndex
    }

    if ($snippets[$snippetIndex].RunName -eq 'system-timeout-clause-trap')
    {
        $systemTimeoutClauseTrapSnippetIndex = $snippetIndex
    }

    if ($snippets[$snippetIndex].RunName -eq 'metrics-listener')
    {
        $metricsSnippetIndex = $snippetIndex
    }

    if ($snippets[$snippetIndex].RunName -eq 'retry-numbers')
    {
        $retryNumbersSnippetIndex = $snippetIndex
    }
}

if ($observableSnippetIndex -lt 0 `
    -or $invalidCompositionSnippetIndex -lt 0 `
    -or $timeoutExceededClauseSnippetIndex -lt 0 `
    -or $systemTimeoutClauseTrapSnippetIndex -lt 0 `
    -or $metricsSnippetIndex -lt 0 `
    -or $retryNumbersSnippetIndex -lt 0)
{
    throw 'A required executable documentation snippet is missing.'
}

[void]$builder.AppendLine("            await Snippet$observableSnippetIndex.RunAsync();")
[void]$builder.AppendLine("            await Snippet$retryNumbersSnippetIndex.RunAsync();")
[void]$builder.AppendLine('        }')
[void]$builder.AppendLine('        finally')
[void]$builder.AppendLine('        {')
[void]$builder.AppendLine('            Console.SetOut(original);')
[void]$builder.AppendLine('        }')
[void]$builder.AppendLine('        var actual = output.ToString().Trim();')
[void]$builder.AppendLine('        var expected = "github: Timeout(30s) → Retry(3, exponential 250ms ×2 +jitter ≤30s) → CircuitBreaker(5 consecutive, break 30s) → ConcurrencyLimit(10, queue 5)" + Environment.NewLine + "1,2,3";')
[void]$builder.AppendLine('        if (!string.Equals(actual, expected, StringComparison.Ordinal))')
[void]$builder.AppendLine('        {')
[void]$builder.AppendLine('            throw new InvalidOperationException($"Documented executable output changed. Expected: {expected}; actual: {actual}");')
[void]$builder.AppendLine('        }')
[void]$builder.AppendLine('        try')
[void]$builder.AppendLine('        {')
[void]$builder.AppendLine("            await Snippet$invalidCompositionSnippetIndex.RunAsync();")
[void]$builder.AppendLine('            throw new InvalidOperationException("Documented invalid composition did not fail fast.");')
[void]$builder.AppendLine('        }')
[void]$builder.AppendLine('        catch (InvalidOperationException exception) when (exception.Message.Contains("Fallback", StringComparison.Ordinal))')
[void]$builder.AppendLine('        {')
[void]$builder.AppendLine('        }')
[void]$builder.AppendLine("        await Snippet$timeoutExceededClauseSnippetIndex.RunAsync();")
[void]$builder.AppendLine("        await Snippet$systemTimeoutClauseTrapSnippetIndex.RunAsync();")
[void]$builder.AppendLine("        await Snippet$metricsSnippetIndex.RunAsync();")
[void]$builder.AppendLine('        Console.WriteLine("Executed documented pipeline behavior successfully.");')
[void]$builder.AppendLine('    }')
[void]$builder.AppendLine('}')

[IO.File]::WriteAllText($generatedPath, $builder.ToString())

[IO.File]::WriteAllText(
    $nugetConfigPath,
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-packages" value="$([Security.SecurityElement]::Escape($resolvedPackagesPath))" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
"@)

& dotnet restore $projectPath --configfile $nugetConfigPath `
    "-p:KevlarPackageVersion=$Version" `
    "-p:GeneratedSnippetsPath=$generatedPath"

if ($LASTEXITCODE -ne 0)
{
    throw "Documentation snippet restore failed with exit code $LASTEXITCODE."
}

foreach ($framework in @('net8.0', 'net10.0'))
{
    & dotnet run --project $projectPath -c Release --framework $framework --no-restore `
        "-p:KevlarPackageVersion=$Version" `
        "-p:GeneratedSnippetsPath=$generatedPath"

    if ($LASTEXITCODE -ne 0)
    {
        throw "Documentation snippet project failed for $framework with exit code $LASTEXITCODE."
    }
}
