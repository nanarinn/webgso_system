$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

$workbook = Join-Path $env:USERPROFILE "Downloads\All_SAND_Forecast_Tables (1).xlsx"
if (-not (Test-Path -LiteralPath $workbook)) {
    throw "Workbook not found: $workbook"
}

$sqlService = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction Stop
if ($sqlService.Status -ne 'Running') { Start-Service -Name 'MSSQL$SQLEXPRESS' }

$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project .\TestingSystem.Web\TestingSystem.Web.csproj -- --import-sand-data $workbook
if ($LASTEXITCODE -ne 0) { throw "BDNS data import failed. Review the error shown above." }

Write-Host "BDNS data imported. Start the system with .\run-local-sqlserver.ps1" -ForegroundColor Green
