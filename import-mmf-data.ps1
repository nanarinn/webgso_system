$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot
$workbook = "C:\Users\aiman\Downloads\Sample_mmf_sent.xlsx"

$sqlService = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction Stop
if ($sqlService.Status -ne 'Running') { Start-Service -Name 'MSSQL$SQLEXPRESS' }

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Logging__LogLevel__Microsoft__EntityFrameworkCore = "Warning"
Write-Host "Saving MMF API payloads, averaging sensor values, and loading MSSQL..." -ForegroundColor Cyan
$importBuild = Join-Path $env:TEMP "sblss-mmf-import"
dotnet build .\TestingSystem.Web\TestingSystem.Web.csproj --no-restore --verbosity quiet --output $importBuild
if ($LASTEXITCODE -ne 0) { throw "MMF import build failed. Review the error shown above." }
Push-Location -LiteralPath $importBuild
try {
    dotnet .\TestingSystem.Web.dll --import-mmf-data $workbook
    if ($LASTEXITCODE -ne 0) { throw "MMF import failed. Review the error shown above." }
}
finally {
    Pop-Location
}
Write-Host "MMF import finished. Restart with .\run-local-sqlserver.ps1" -ForegroundColor Green
