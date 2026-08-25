[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$errors = [System.Collections.Generic.List[string]]::new()
$requiredFiles = @(
    'CONTRIBUTING.md'
    'SECURITY.md'
    'CODE_OF_CONDUCT.md'
    '.github/PULL_REQUEST_TEMPLATE.md'
    '.github/ISSUE_TEMPLATE/bug_report.yml'
    '.github/ISSUE_TEMPLATE/feature_request.yml'
    '.github/ISSUE_TEMPLATE/question.yml'
    '.github/ISSUE_TEMPLATE/config.yml'
)

foreach ($relativePath in $requiredFiles)
{
    $path = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf))
    {
        $errors.Add("Missing repository file '$relativePath'.")
        continue
    }

    if ([string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $path -Raw)))
    {
        $errors.Add("Repository file '$relativePath' is empty.")
    }
}

$readme = Get-Content -LiteralPath (Join-Path $repositoryRoot 'README.md') -Raw
$badgeCount = ([regex]::Matches($readme, '\[!\[[^\]]+\]\([^\)]+\)\]\([^\)]+\)')).Count
if ($badgeCount -lt 5)
{
    $errors.Add("README.md must contain at least five linked badges; found $badgeCount.")
}

$requiredReadmeLinks = @(
    'CONTRIBUTING.md'
    'SECURITY.md'
    'CODE_OF_CONDUCT.md'
    'CHANGELOG.md'
)

foreach ($requiredLink in $requiredReadmeLinks)
{
    $url = "https://github.com/thomhurst/Kevlar/blob/main/$requiredLink"
    if ($readme -notmatch [regex]::Escape("]($url)"))
    {
        $errors.Add("README.md does not link to canonical '$url'.")
    }
}

$testing = Get-Content -LiteralPath (Join-Path $repositoryRoot 'docs/docs/testing.md') -Raw
if ($testing.Contains('## Repository quality gates', [StringComparison]::Ordinal))
{
    $errors.Add('Contributor-only repository gates must live in CONTRIBUTING.md, not testing.md.')
}

if ($errors.Count -gt 0)
{
    throw "Repository verification failed:`n- $($errors -join "`n- ")"
}

& node (Join-Path $repositoryRoot 'docs/scripts/verify-issue-forms.mjs')
if ($LASTEXITCODE -ne 0)
{
    throw "Issue-form verification failed with exit code $LASTEXITCODE. Run 'npm ci' under docs first."
}

Write-Host "Verified $($requiredFiles.Count) community files, README badges and links, and issue-form YAML."
