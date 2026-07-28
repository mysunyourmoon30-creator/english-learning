[CmdletBinding()]
param(
    [string]$Repository,
    [string]$Branch = 'main'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

gh auth status | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI is not authenticated. Run gh auth login and retry.'
}

if ([string]::IsNullOrWhiteSpace($Repository)) {
    $Repository = gh repo view --json nameWithOwner --jq '.nameWithOwner'
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($Repository)) {
        throw 'Could not resolve the GitHub repository.'
    }
}

if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "Invalid repository name: $Repository"
}
if ($Branch -notmatch '^[A-Za-z0-9._/-]+$') {
    throw "Invalid branch name: $Branch"
}

$protection = @{
    required_status_checks = @{
        strict = $true
        contexts = @(
            'CI / verify',
            'CodeQL / analyze',
            'PostgreSQL migration and restore / integration',
            'Playwright E2E / learner-journey'
        )
    }
    enforce_admins = $true
    required_pull_request_reviews = @{
        dismiss_stale_reviews = $true
        require_code_owner_reviews = $false
        required_approving_review_count = 1
        require_last_push_approval = $true
    }
    restrictions = $null
    required_conversation_resolution = $true
    allow_force_pushes = $false
    allow_deletions = $false
    block_creations = $false
} | ConvertTo-Json -Depth 8

$security = @{
    security_and_analysis = @{
        secret_scanning = @{ status = 'enabled' }
        secret_scanning_push_protection = @{ status = 'enabled' }
    }
} | ConvertTo-Json -Depth 6

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "englishmaster-github-security-$([Guid]::NewGuid().ToString('N'))")
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $protectionPath = Join-Path $temporaryRoot 'protection.json'
    $securityPath = Join-Path $temporaryRoot 'security.json'
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($protectionPath, $protection, $utf8NoBom)
    [IO.File]::WriteAllText($securityPath, $security, $utf8NoBom)

    gh api --method PUT `
        -H 'Accept: application/vnd.github+json' `
        "/repos/$Repository/branches/$Branch/protection" `
        --input $protectionPath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not apply branch protection.'
    }

    gh api --method PATCH `
        -H 'Accept: application/vnd.github+json' `
        "/repos/$Repository" `
        --input $securityPath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not enable secret scanning and push protection.'
    }

    gh api --method PUT `
        -H 'Accept: application/vnd.github+json' `
        "/repos/$Repository/vulnerability-alerts" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not enable Dependabot vulnerability alerts.'
    }

    gh api --method PUT `
        -H 'Accept: application/vnd.github+json' `
        "/repos/$Repository/automated-security-fixes" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not enable Dependabot security updates.'
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Output (
    "Configured branch protection, required checks, secret scanning, push protection, " +
    "vulnerability alerts, and security updates for $Repository ($Branch).")
