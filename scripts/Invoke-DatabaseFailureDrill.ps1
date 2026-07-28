[CmdletBinding()]
param(
    [string]$ComposeFile,
    [uri]$ReadinessUri = 'http://127.0.0.1:8080/health/ready',
    [ValidateRange(5, 120)]
    [int]$DurationSeconds = 15,
    [switch]$ConfirmDisruption
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ComposeFile)) {
    $ComposeFile = Join-Path (
        Split-Path $PSScriptRoot -Parent) 'deploy\staging\compose.yaml'
}
if (-not $ConfirmDisruption) {
    throw 'This drill pauses the staging database. Re-run with -ConfirmDisruption.'
}

$composePath = (Resolve-Path -LiteralPath $ComposeFile).Path
$readinessFailed = $false
docker compose --file $composePath pause postgres
if ($LASTEXITCODE -ne 0) {
    throw 'Could not pause the staging PostgreSQL container.'
}

try {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($DurationSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest `
                -Uri $ReadinessUri `
                -UseBasicParsing `
                -TimeoutSec 3
            if ($response.StatusCode -ne 200) {
                $readinessFailed = $true
                break
            }
        }
        catch {
            $readinessFailed = $true
            break
        }
        Start-Sleep -Seconds 1
    }
}
finally {
    docker compose --file $composePath unpause postgres
    if ($LASTEXITCODE -ne 0) {
        throw 'CRITICAL: Could not unpause the staging PostgreSQL container.'
    }
}

if (-not $readinessFailed) {
    throw 'Readiness stayed healthy while PostgreSQL was paused.'
}

for ($attempt = 1; $attempt -le 60; $attempt++) {
    try {
        $response = Invoke-WebRequest `
            -Uri $ReadinessUri `
            -UseBasicParsing `
            -TimeoutSec 3
        if ($response.StatusCode -eq 200) {
            Write-Output (
                'Database failure drill passed: readiness failed closed and recovered.')
            exit 0
        }
    }
    catch {
        Start-Sleep -Seconds 2
    }
}

throw 'Database recovered but application readiness did not.'
