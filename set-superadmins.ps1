param(
    [string[]]$Emails = @(
        "aiman@sae-malaysia.com",
        "nadera@sae-malaysia.com"
    )
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

if ($Emails.Count -eq 0) {
    throw "Provide at least one SBLSS account email."
}

Write-Host "Assigning the SBLSS Super Admin role..." -ForegroundColor Cyan
dotnet run --project .\TestingSystem.Web\TestingSystem.Web.csproj --no-launch-profile `
    --environment Development -- --set-superadmins @Emails
if ($LASTEXITCODE -ne 0) {
    throw "The Super Admin update failed. Review the database error shown above."
}

Write-Host "Super Admin assignment completed. Users must sign out and sign in again." -ForegroundColor Green
