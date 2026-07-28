[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AppImage,
    [string]$PreviousImage,
    [string]$ComposeFile,
    [uri]$BaseUri = 'http://127.0.0.1:8080',
    [switch]$RunBackupRestoreDrill,
    [switch]$ConfirmDeploy
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ComposeFile)) {
    $ComposeFile = Join-Path (
        Split-Path $PSScriptRoot -Parent) 'deploy\staging\compose.yaml'
}
if (-not $ConfirmDeploy) {
    throw 'Staging deployment changes running containers. Re-run with -ConfirmDeploy.'
}

$digestPattern = '^ghcr\.io/[a-z0-9_.-]+/[a-z0-9_.-]+@sha256:[a-f0-9]{64}$'
if ($AppImage -notmatch $digestPattern) {
    throw 'AppImage must be an immutable lowercase GHCR sha256 digest reference.'
}
if (-not [string]::IsNullOrWhiteSpace($PreviousImage) -and
    $PreviousImage -notmatch $digestPattern) {
    throw 'PreviousImage must be an immutable lowercase GHCR sha256 digest reference.'
}

$composePath = (Resolve-Path -LiteralPath $ComposeFile).Path
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$env:APP_IMAGE = $AppImage

function Wait-Ready {
    param([int]$Attempts = 60)
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            $response = Invoke-WebRequest `
                -Uri ([uri]::new($BaseUri, '/health/ready')) `
                -UseBasicParsing `
                -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }
    throw "Staging readiness did not recover at $BaseUri."
}

Push-Location -LiteralPath $repositoryRoot
try {
    if ($RunBackupRestoreDrill) {
        & (Join-Path $PSScriptRoot 'Verify-PostgresBackup.ps1') `
            -ComposeFile $composePath
    }

    docker compose --file $composePath pull migration web
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not pull the immutable staging image.'
    }

    docker compose --file $composePath run --rm migration
    if ($LASTEXITCODE -ne 0) {
        throw 'Migration job failed; web instances were not changed.'
    }

    docker compose --file $composePath up --detach --no-deps web
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not start the new staging image.'
    }

    try {
        Wait-Ready
        & (Join-Path $PSScriptRoot 'Test-ReadinessUnderLoad.ps1') `
            -BaseUri $BaseUri
    }
    catch {
        if ([string]::IsNullOrWhiteSpace($PreviousImage)) {
            throw
        }

        Write-Warning 'New image failed readiness. Rolling back to PreviousImage.'
        $env:APP_IMAGE = $PreviousImage
        docker compose --file $composePath up --detach --no-deps web
        if ($LASTEXITCODE -ne 0) {
            throw 'Image rollback failed.'
        }
        Wait-Ready
        throw "New image failed; image rollback completed. $($_.Exception.Message)"
    }
}
finally {
    Pop-Location
}

Write-Output "Staging release passed migration, readiness, and load smoke checks."
