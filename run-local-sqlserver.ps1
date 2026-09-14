$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

$sqlService = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction Stop
if ($sqlService.Status -ne 'Running') {
    Start-Service -Name 'MSSQL$SQLEXPRESS'
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$port = 5186
$wifiAddress = [System.Net.Dns]::GetHostAddresses($env:COMPUTERNAME) |
    Where-Object { $_.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork -and $_.ToString() -like '192.168.*' } |
    Select-Object -First 1

Write-Host "Local access:   http://localhost:$port" -ForegroundColor Cyan
if ($wifiAddress) {
    Write-Host "Network access: http://$($wifiAddress):$port" -ForegroundColor Green
}

dotnet run --project .\TestingSystem.Web --urls "http://0.0.0.0:$port"
