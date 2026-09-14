$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

$sqlService = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction Stop
if ($sqlService.Status -ne 'Running') {
    Start-Service -Name 'MSSQL$SQLEXPRESS'
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project .\TestingSystem.Web
