$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

$sqlService = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction Stop
if ($sqlService.Status -ne 'Running') {
    Start-Service -Name 'MSSQL$SQLEXPRESS'
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
Write-Host "Resetting the complete SBLSS development workflow in Microsoft SQL Server..." -ForegroundColor Cyan
Write-Host "This recreates related plants, API configurations, collections, readings, schedules, anomalies, reports, recipients and deliveries." -ForegroundColor DarkCyan
dotnet run --project .\TestingSystem.Web\TestingSystem.Web.csproj -- --seed-dummy-api

if ($LASTEXITCODE -ne 0) {
    throw "Simulated API data setup failed. Review the error shown above."
}

Write-Host "Setup finished. Start the system with .\run-local-sqlserver.ps1" -ForegroundColor Green
