[CmdletBinding()]
param(
    [string]$ComposeFile,
    [string]$OutputDirectory,
    [string]$Database = 'englishmaster',
    [string]$DatabaseUser = 'englishmaster'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($ComposeFile)) {
    $ComposeFile = Join-Path $repositoryRoot 'compose.yaml'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\backups'
}
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
