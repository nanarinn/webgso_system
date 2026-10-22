$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $dotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
}
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw ".NET 8 SDK was not found."
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:UiOnly__Enabled = "true"
$env:ASPNETCORE_URLS = "http://localhost:5186"

Write-Host "Starting WEBGSO in UI-only mode..." -ForegroundColor Cyan
Write-Host "No SQL Server, MySQL or PostgreSQL connection will be used." -ForegroundColor Yellow
Write-Host "Open: http://localhost:5186" -ForegroundColor Green
Write-Host "Temporary preview data resets whenever the application restarts." -ForegroundColor DarkYellow

& $dotnet run --project .\TestingSystem.Web\TestingSystem.Web.csproj --no-launch-profile --no-restore
