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

$repositoryJson = gh repo view $Repository `
    --json nameWithOwner,isPrivate,viewerPermission
if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect repository access for $Repository."
}
$repositoryInfo = $repositoryJson | ConvertFrom-Json
if ($repositoryInfo.viewerPermission -ne 'ADMIN') {
    throw "Admin permission is required for $Repository."
}

function Test-PlanRestriction {
    param([string]$Message)

    return $Message -match 'Upgrade to GitHub Pro' `
        -or $Message -match 'make this repository public' `
        -or $Message -match 'GitHub Advanced Security' `
        -or $Message -match 'not available for (this|private) repositor'
}

function Invoke-GhCapture {
    param([string[]]$Arguments)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        # Windows PowerShell converts native stderr into ErrorRecord objects.
        # Capture known API failures without allowing ErrorActionPreference=Stop
        # to terminate before the caller can classify a GitHub plan restriction.
        $ErrorActionPreference = 'Continue'
        $output = & gh @Arguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
        return [pscustomobject]@{
            ExitCode = $exitCode
            Output = $output
        }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

# Contexts must be the check-run names GitHub actually reports, which are the
# bare job ids. Naming them 'CI / verify' style leaves a pull request waiting
# forever on checks that never report under that name.
$protection = @{
    required_status_checks = @{
        strict = $true
        contexts = @(
            'verify',
            'analyze',
            'integration',
            'learner-journey'
        )
    }
    enforce_admins = $true
    # Zero approvals because this repository has a single collaborator and
    # GitHub does not allow approving your own pull request. Asking for one
    # here, with enforce_admins on, makes the branch unmergeable rather than
    # protected — including by the pull request that would undo it. Raise this
    # to 1 (and consider require_last_push_approval) once a second person can
    # review. Pull requests and passing checks are still required either way.
    required_pull_request_reviews = @{
        dismiss_stale_reviews = $true
        require_code_owner_reviews = $false
        required_approving_review_count = 0
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
$applied = [Collections.Generic.List[string]]::new()
$skipped = [Collections.Generic.List[string]]::new()
try {
    $protectionPath = Join-Path $temporaryRoot 'protection.json'
    $securityPath = Join-Path $temporaryRoot 'security.json'
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($protectionPath, $protection, $utf8NoBom)
    [IO.File]::WriteAllText($securityPath, $security, $utf8NoBom)

    $protectionCall = Invoke-GhCapture -Arguments @(
        'api',
        '--method', 'PUT',
        '-H', 'Accept: application/vnd.github+json',
        "/repos/$Repository/branches/$Branch/protection",
        '--input', $protectionPath)
    if ($protectionCall.ExitCode -eq 0) {
        $applied.Add('branch protection and required checks')
    }
    elseif ($repositoryInfo.isPrivate -and
        (Test-PlanRestriction $protectionCall.Output)) {
        $skipped.Add(
            'branch protection (requires GitHub Pro or a public repository)')
        Write-Warning (
            "GitHub plan restriction: branch protection was not applied to " +
            "private repository $Repository.")
    }
    else {
        throw "Could not apply branch protection. $($protectionCall.Output)"
    }

    $secretScanningCall = Invoke-GhCapture -Arguments @(
        'api',
        '--method', 'PATCH',
        '-H', 'Accept: application/vnd.github+json',
        "/repos/$Repository",
        '--input', $securityPath)
    if ($secretScanningCall.ExitCode -eq 0) {
        $applied.Add('secret scanning and push protection')
    }
    elseif ($repositoryInfo.isPrivate -and
        (Test-PlanRestriction $secretScanningCall.Output)) {
        $skipped.Add(
            'secret scanning/push protection (not included for this private repository plan)')
        Write-Warning (
            "GitHub plan restriction: secret scanning and push protection " +
            "were not enabled for $Repository.")
    }
    else {
        throw (
            "Could not enable secret scanning and push protection. " +
            $secretScanningCall.Output)
    }

    gh api --method PUT `
        -H 'Accept: application/vnd.github+json' `
        "/repos/$Repository/vulnerability-alerts" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not enable Dependabot vulnerability alerts.'
    }
    $applied.Add('Dependabot vulnerability alerts')

    gh api --method PUT `
        -H 'Accept: application/vnd.github+json' `
        "/repos/$Repository/automated-security-fixes" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not enable Dependabot security updates.'
    }
    $applied.Add('Dependabot security updates')
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Output "Applied to $Repository ($Branch): $($applied -join ', ')."
if ($skipped.Count -gt 0) {
    Write-Warning "Skipped because of GitHub plan limits: $($skipped -join ', ')."
    Write-Output (
        'The repository was not made public automatically. Upgrade the account ' +
        'or explicitly approve public visibility before retrying unavailable features.')
}
