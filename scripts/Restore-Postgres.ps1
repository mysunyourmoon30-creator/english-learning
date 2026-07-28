[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BackupPath,
    [Parameter(Mandatory)]
    [string]$TargetDatabase,
    [switch]$ConfirmRestore,
    [string]$ComposeFile = (Join-Path (Split-Path $PSScriptRoot -Parent) 'compose.yaml'),
    [string]$DatabaseUser = 'englishmaster'
)

$ErrorActionPreference = 'Stop'
if (-not $ConfirmRestore) {
    throw 'Restore replaces the target database. Re-run with -ConfirmRestore.'
}
if ($TargetDatabase -in @('englishmaster', 'postgres', 'template0', 'template1')) {
    throw "Refusing to replace protected database '$TargetDatabase'. Use a new restore-test database."
}

$resolvedBackup = (Resolve-Path -LiteralPath $BackupPath).Path
$composePath = (Resolve-Path -LiteralPath $ComposeFile).Path
$containerFile = "/tmp/$([IO.Path]::GetFileName($resolvedBackup))"

& docker compose --file $composePath cp $resolvedBackup "postgres:$containerFile"
if ($LASTEXITCODE -ne 0) { throw 'Could not copy the backup into PostgreSQL.' }

try {
    & docker compose --file $composePath exec -T postgres `
        dropdb -U $DatabaseUser --if-exists $TargetDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Could not clear the restore target.' }

    & docker compose --file $composePath exec -T postgres `
        createdb -U $DatabaseUser $TargetDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the restore target.' }

    & docker compose --file $composePath exec -T postgres `
        pg_restore -U $DatabaseUser -d $TargetDatabase --exit-on-error $containerFile
    if ($LASTEXITCODE -ne 0) { throw 'pg_restore failed.' }
}
finally {
    & docker compose --file $composePath exec -T postgres rm -f $containerFile
}

Write-Output "Restored into '$TargetDatabase'."
