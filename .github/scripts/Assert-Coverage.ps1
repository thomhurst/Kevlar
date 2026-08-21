[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Report,

    [Parameter(Mandatory)]
    [ValidateRange(0, 100)]
    [decimal]$MinimumLinePercent,

    [Parameter(Mandatory)]
    [ValidateRange(0, 100)]
    [decimal]$MinimumBranchPercent
)

$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture
$resolvedReport = Resolve-Path -LiteralPath $Report
[xml]$coverage = Get-Content -LiteralPath $resolvedReport -Raw
$root = $coverage.coverage

if ($null -eq $root -or $null -eq $root.packages.package) {
    throw "Coverage report '$resolvedReport' contains no packages."
}

$validLines = [int]::Parse($root.'lines-valid', $culture)
$validBranches = [int]::Parse($root.'branches-valid', $culture)
if ($validLines -eq 0 -or $validBranches -eq 0) {
    throw "Coverage report '$resolvedReport' contains no measurable lines or branches."
}

$linePercent = [decimal]::Parse($root.'line-rate', $culture) * 100
$branchPercent = [decimal]::Parse($root.'branch-rate', $culture) * 100
Write-Host ('Coverage: lines {0:F2}% (minimum {1:F2}%), branches {2:F2}% (minimum {3:F2}%).' -f
    $linePercent, $MinimumLinePercent, $branchPercent, $MinimumBranchPercent)

$failures = [System.Collections.Generic.List[string]]::new()
if ($linePercent -lt $MinimumLinePercent) {
    $failures.Add("Line coverage $($linePercent.ToString('F2', $culture))% is below $($MinimumLinePercent.ToString('F2', $culture))%.")
}

if ($branchPercent -lt $MinimumBranchPercent) {
    $failures.Add("Branch coverage $($branchPercent.ToString('F2', $culture))% is below $($MinimumBranchPercent.ToString('F2', $culture))%.")
}

if ($failures.Count -gt 0) {
    throw ($failures -join ' ')
}
