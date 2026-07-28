[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$CoverageFile,
    [ValidateRange(0, 100)]
    [double]$MinimumLinePercent = 60,
    [ValidateRange(0, 100)]
    [double]$MinimumBranchPercent = 40
)

$lineCovered = 0L
$lineValid = 0L
$branchCovered = 0L
$branchValid = 0L

foreach ($path in $CoverageFile) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Coverage report not found: $path"
    }

    [xml]$report = Get-Content -LiteralPath $path
    $coverage = $report.coverage
    $lineCovered += [long]$coverage.'lines-covered'
    $lineValid += [long]$coverage.'lines-valid'
    $branchCovered += [long]$coverage.'branches-covered'
    $branchValid += [long]$coverage.'branches-valid'
}

if ($CoverageFile.Count -eq 0 -or $lineValid -eq 0 -or $branchValid -eq 0) {
    throw "No usable Cobertura coverage reports were supplied."
}

$linePercent = [Math]::Round(100 * $lineCovered / $lineValid, 2)
$branchPercent = [Math]::Round(100 * $branchCovered / $branchValid, 2)
Write-Host (
    "Coverage: line {0:N2}% ({1}/{2}), branch {3:N2}% ({4}/{5})" -f
    $linePercent,
    $lineCovered,
    $lineValid,
    $branchPercent,
    $branchCovered,
    $branchValid)
Write-Host (
    "Required: line {0:N2}%, branch {1:N2}%" -f
    $MinimumLinePercent,
    $MinimumBranchPercent)

if ($linePercent -lt $MinimumLinePercent) {
    throw "Line coverage $linePercent% is below the $MinimumLinePercent% threshold."
}

if ($branchPercent -lt $MinimumBranchPercent) {
    throw "Branch coverage $branchPercent% is below the $MinimumBranchPercent% threshold."
}
