$ErrorActionPreference = 'Stop'

Set-Location -LiteralPath $PSScriptRoot

$localDotnet = Join-Path $PSScriptRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }

& $dotnet run --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj'
