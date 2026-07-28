[CmdletBinding()]
param(
    [string]$ManifestPath,
    [ValidateSet('Draft', 'Production')]
    [string]$Mode = 'Draft',
    [int]$ExpectedListeningAssets = 100,
    [int]$ExpectedPartOneImages = 6
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path (
        Split-Path $PSScriptRoot -Parent) 'content\toeic-media\manifest.json'
}
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.version -ne 1 -or $null -eq $manifest.assets) {
    throw 'TOEIC media manifest must use version 1 and contain an assets array.'
}

$allowedAccents = @('US', 'UK', 'Australian', 'Canadian')
$hexPattern = '^[a-fA-F0-9]{64}$'
$contentKeys = @{}
foreach ($asset in @($manifest.assets)) {
    if ($asset.contentKey -notmatch $hexPattern) {
        throw 'Every TOEIC asset requires a 64-character contentKey.'
    }
    if ($contentKeys.ContainsKey($asset.contentKey)) {
        throw "Duplicate TOEIC content key: $($asset.contentKey)"
    }
    $contentKeys[$asset.contentKey] = $true
    if ($asset.part -lt 1 -or $asset.part -gt 4) {
        throw "Listening asset part must be between 1 and 4: $($asset.contentKey)"
    }
    if ($asset.audioObjectKey -notmatch $hexPattern -or
        $asset.sha256 -notmatch $hexPattern) {
        throw "Asset $($asset.contentKey) requires audioObjectKey and SHA-256."
    }
    if ($asset.accent -notin $allowedAccents) {
        throw "Asset $($asset.contentKey) has unsupported accent '$($asset.accent)'."
    }
    if ($asset.sourceType -ne 'LicensedHumanRecording' -or
        [string]::IsNullOrWhiteSpace($asset.licenseId) -or
        [string]::IsNullOrWhiteSpace($asset.licenseEvidencePath)) {
        throw "Asset $($asset.contentKey) lacks human-source license evidence."
    }
    if ($asset.integratedLufs -lt -18 -or $asset.integratedLufs -gt -14) {
        throw "Asset $($asset.contentKey) must be normalized between -18 and -14 LUFS."
    }
    if ($asset.truePeakDb -gt -1) {
        throw "Asset $($asset.contentKey) exceeds the -1 dBTP ceiling."
    }
    if (-not $asset.expertClarityApproved -or -not $asset.approved -or
        [string]::IsNullOrWhiteSpace($asset.approvedBy) -or
        [string]::IsNullOrWhiteSpace($asset.approvedAtUtc)) {
        throw "Asset $($asset.contentKey) has not completed expert approval."
    }
    if ($asset.part -eq 1 -and (
        [string]::IsNullOrWhiteSpace($asset.imageUrl) -or
        -not $asset.imageUrl.StartsWith('https://') -or
        [string]::IsNullOrWhiteSpace($asset.imageLicenseId))) {
        throw "Part 1 asset $($asset.contentKey) requires a licensed HTTPS image."
    }
}

if ($Mode -eq 'Production') {
    $assets = @($manifest.assets)
    if ($assets.Count -ne $ExpectedListeningAssets) {
        throw "Production requires exactly $ExpectedListeningAssets listening assets; found $($assets.Count)."
    }
    $partOneImageCount = @(
        $assets | Where-Object {
            $_.part -eq 1 -and -not [string]::IsNullOrWhiteSpace($_.imageUrl)
        }).Count
    if ($partOneImageCount -ne $ExpectedPartOneImages) {
        throw "Production requires exactly $ExpectedPartOneImages licensed Part 1 images; found $partOneImageCount."
    }
    foreach ($accent in $allowedAccents) {
        if (-not ($assets | Where-Object accent -eq $accent)) {
            throw "Production catalog has no '$accent' accent recording."
        }
    }
}

Write-Output (
    "TOEIC media manifest passed $Mode validation with $(@($manifest.assets).Count) asset(s).")
