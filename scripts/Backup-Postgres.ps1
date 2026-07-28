[CmdletBinding()]
param(
    [string]$ComposeFile = (Join-Path (Split-Path $PSScriptRoot -Parent) 'compose.yaml'),
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\backups'),
    [string]$Database = 'englishmaster',
    [string]$DatabaseUser = 'englishmaster'
)

$ErrorActionPreference = 'Stop'
$composePath = (Resolve-Path -LiteralPath $ComposeFile).Path
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outputPath = Join-Path (Resolve-Path -LiteralPath $OutputDirectory).Path (
    '{0}-{1:yyyyMMdd-HHmmss}.dump' -f $Database, [DateTimeOffset]::UtcNow)
$containerFile = "/tmp/$([IO.Path]::GetFileName($outputPath))"

& docker compose --file $composePath exec -T postgres `
    pg_dump -U $DatabaseUser -d $Database --format=custom --file=$containerFile
if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed.' }

try {
    & docker compose --file $composePath cp "postgres:$containerFile" $outputPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy the backup from PostgreSQL.' }
}
finally {
    & docker compose --file $composePath exec -T postgres rm -f $containerFile
}

Write-Output $outputPath
