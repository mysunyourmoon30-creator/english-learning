$ErrorActionPreference = 'Stop'

Set-Location -LiteralPath $PSScriptRoot
dotnet run --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj'
