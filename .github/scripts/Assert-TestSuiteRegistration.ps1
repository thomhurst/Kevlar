[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')
$solution = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Kevlar.slnx') -Raw
$workflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/ci.yml') -Raw
$failures = [System.Collections.Generic.List[string]]::new()

Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'tests') -Recurse -Filter '*.csproj' |
    Sort-Object FullName |
    ForEach-Object {
        [xml]$project = Get-Content -LiteralPath $_.FullName -Raw
        $usesTUnit = @($project.Project.ItemGroup.PackageReference |
            Where-Object { $_.Include -eq 'TUnit' }).Count -gt 0
        if (-not $usesTUnit)
        {
            return
        }

        $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName).Replace('\', '/')
        $projectDirectory = [IO.Path]::GetDirectoryName($relativePath).Replace('\', '/')
        if (-not $solution.Contains("Project Path=`"$relativePath`"", [StringComparison]::Ordinal))
        {
            $failures.Add("TUnit project '$relativePath' is missing from Kevlar.slnx.")
        }

        $runPattern = "--project\s+['`"]?" +
            [regex]::Escape($projectDirectory) +
            "(?:/" + [regex]::Escape($_.Name) + ")?['`"]?(?:\s|$)"
        if ($workflow -notmatch $runPattern)
        {
            $failures.Add("TUnit project '$relativePath' is not run by .github/workflows/ci.yml.")
        }
    }

if ($failures.Count -gt 0)
{
    throw ($failures -join [Environment]::NewLine)
}

Write-Host 'Every TUnit project is registered in Kevlar.slnx and CI.'
