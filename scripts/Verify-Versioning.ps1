[CmdletBinding()]
param(
    [string]$GitVersionCommand = 'dotnet-gitversion'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "kevlar-versioning-$([Guid]::NewGuid().ToString('N'))"
$resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
[IO.Directory]::CreateDirectory($resolvedTemporaryRoot) | Out-Null

function Invoke-Git([string]$RepositoryPath, [string[]]$Arguments)
{
    & git -C $RepositoryPath @Arguments *> $null
    if ($LASTEXITCODE -ne 0)
    {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-Version([string]$Name, [string]$CommitMessage, [string]$ExpectedVersion)
{
    $scenarioPath = Join-Path $resolvedTemporaryRoot $Name
    [IO.Directory]::CreateDirectory($scenarioPath) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'GitVersion.yml') -Destination $scenarioPath
    [IO.File]::WriteAllText(
        (Join-Path $scenarioPath 'content.txt'),
        'tagged',
        [Text.UTF8Encoding]::new($false))

    Invoke-Git $scenarioPath @('init', '--initial-branch=main')
    Invoke-Git $scenarioPath @('config', 'user.name', 'Kevlar Version Test')
    Invoke-Git $scenarioPath @('config', 'user.email', 'version-test@kevlar.invalid')
    Invoke-Git $scenarioPath @('add', '.')
    Invoke-Git $scenarioPath @('commit', '-m', 'chore: tagged baseline')
    Invoke-Git $scenarioPath @('tag', 'v0.10.0')

    [IO.File]::AppendAllText(
        (Join-Path $scenarioPath 'content.txt'),
        [Environment]::NewLine + $Name,
        [Text.UTF8Encoding]::new($false))
    Invoke-Git $scenarioPath @('add', 'content.txt')
    Invoke-Git $scenarioPath @('commit', '-m', $CommitMessage)

    $githubActions = $env:GITHUB_ACTIONS
    $githubRef = $env:GITHUB_REF
    $githubHeadRef = $env:GITHUB_HEAD_REF
    $githubBaseRef = $env:GITHUB_BASE_REF
    $output = ''
    $exitCode = -1
    try
    {
        $env:GITHUB_ACTIONS = $null
        $env:GITHUB_REF = $null
        $env:GITHUB_HEAD_REF = $null
        $env:GITHUB_BASE_REF = $null
        $output = (& $GitVersionCommand $scenarioPath /showvariable SemVer 2>&1 | Out-String).Trim()
        $exitCode = $LASTEXITCODE
    }
    finally
    {
        $env:GITHUB_ACTIONS = $githubActions
        $env:GITHUB_REF = $githubRef
        $env:GITHUB_HEAD_REF = $githubHeadRef
        $env:GITHUB_BASE_REF = $githubBaseRef
    }

    if ($exitCode -ne 0)
    {
        throw "$GitVersionCommand failed for $Name with exit code $exitCode.`n$output"
    }

    if ($output -ne $ExpectedVersion)
    {
        throw "$Name version: expected '$ExpectedVersion', got '$output'."
    }
}

try
{
    Assert-Version 'patch' 'chore: ordinary change' '0.10.1'
    Assert-Version 'minor' 'feat: minor change +semver:minor' '0.11.0'
    Assert-Version 'major' 'feat!: breaking change +semver:major' '1.0.0'
}
finally
{
    if ($resolvedTemporaryRoot.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase))
    {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}

Write-Host 'GitVersion patch, minor, and major scenarios passed.'
