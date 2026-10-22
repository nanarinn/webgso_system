$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

$service = Get-Service -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^postgresql-' } |
    Select-Object -First 1
if (-not $service -or $service.Status -ne "Running") {
    throw "PostgreSQL Server is not running. Start the PostgreSQL Windows service first."
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { throw ".NET 8 SDK was not found." }

$secretLine = @(& $dotnet user-secrets list --project .\TestingSystem.Web\TestingSystem.Web.csproj 2>$null |
    Where-Object { $_ -match '^ConnectionStrings:DefaultConnection\s*=' }) | Select-Object -First 1
if (-not $secretLine) {
    throw "PostgreSQL is not configured yet. Run .\setup-postgresql.cmd first."
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:UiOnly__Enabled = "false"
$env:ASPNETCORE_URLS = "http://localhost:5186"

Write-Host "Starting WEBGSO with PostgreSQL..." -ForegroundColor Cyan
Write-Host "Open: http://localhost:5186" -ForegroundColor Green
& $dotnet run --project .\TestingSystem.Web\TestingSystem.Web.csproj --no-launch-profile --no-restore
