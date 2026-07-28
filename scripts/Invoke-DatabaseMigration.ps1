[CmdletBinding()]
param(
    [string]$ComposeFile = "compose.yaml",
    [switch]$Pull
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resolvedCompose = if ([System.IO.Path]::IsPathRooted($ComposeFile)) {
    $ComposeFile
}
else {
    Join-Path $repositoryRoot $ComposeFile
}

if (-not (Test-Path -LiteralPath $resolvedCompose -PathType Leaf)) {
    throw "Compose file not found: $resolvedCompose"
}

Push-Location -LiteralPath $repositoryRoot
try {
    if ($Pull) {
        docker compose --file $resolvedCompose pull migration
        if ($LASTEXITCODE -ne 0) {
            throw "Could not pull the migration image."
        }
    }

    docker compose --file $resolvedCompose run --rm migration
    if ($LASTEXITCODE -ne 0) {
        throw "The one-off migration job failed. Application instances were not started."
    }
}
finally {
    Pop-Location
}
