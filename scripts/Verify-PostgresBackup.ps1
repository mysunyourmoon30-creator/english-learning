[CmdletBinding()]
param(
    [string]$ComposeFile,
    [string]$Database = 'englishmaster',
    [string]$DatabaseUser = 'englishmaster'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ComposeFile)) {
    $ComposeFile = Join-Path (Split-Path $PSScriptRoot -Parent) 'compose.yaml'
}
$restoreDatabase = 'englishmaster_restore_test_{0:yyyyMMddHHmmss}' -f [DateTimeOffset]::UtcNow
$backup = & (Join-Path $PSScriptRoot 'Backup-Postgres.ps1') `
    -ComposeFile $ComposeFile `
    -Database $Database `
    -DatabaseUser $DatabaseUser

try {
    & (Join-Path $PSScriptRoot 'Restore-Postgres.ps1') `
        -BackupPath $backup `
        -TargetDatabase $restoreDatabase `
        -ConfirmRestore `
        -ComposeFile $ComposeFile `
        -DatabaseUser $DatabaseUser

    $composePath = (Resolve-Path -LiteralPath $ComposeFile).Path
    $migrationCount = & docker compose --file $composePath exec -T postgres `
        psql -U $DatabaseUser -d $restoreDatabase -Atc `
        'SELECT COUNT(*) FROM "__EFMigrationsHistory";'
    if ($LASTEXITCODE -ne 0 -or [int]$migrationCount -lt 1) {
        throw 'Restore verification failed: migration history is missing.'
    }

    Write-Output "Backup restore verified with $migrationCount migration(s)."
}
finally {
    $composePath = (Resolve-Path -LiteralPath $ComposeFile).Path
    & docker compose --file $composePath exec -T postgres `
        dropdb -U $DatabaseUser --if-exists $restoreDatabase
}
