param(
    [string[]]$Emails
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

if (-not $Emails -or $Emails.Count -eq 0) {
    throw "Provide at least one SBLSS account email."
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $dotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
}
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw ".NET 8 SDK was not found. Install the .NET 8 SDK, then run this script again."
}

if (-not (Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue) -and
    (Get-Service -Name 'MSSQLSERVER' -ErrorAction SilentlyContinue)) {
    $env:ConnectionStrings__DefaultConnection = 'Server=.;Database=SingleBuyerLssRepository;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;'
    $env:Database__Server = '.'
    $env:Database__Port = '0'
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'

Write-Host "Assigning the SBLSS Super Admin role..." -ForegroundColor Cyan
& $dotnet run --project .\TestingSystem.Web\TestingSystem.Web.csproj --no-launch-profile `
    -- --set-superadmins @Emails
if ($LASTEXITCODE -ne 0) {
    throw "The Super Admin update failed. Review the database error shown above."
}

Write-Host "Super Admin assignment completed. Users must sign out and sign in again." -ForegroundColor Green
