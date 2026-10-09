param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SenderMailbox
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectRoot "TestingSystem.Web\TestingSystem.Web.csproj"
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $dotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
}
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw '.NET 8 SDK was not found. Install the .NET 8 SDK, then run this script again.'
}

if (-not [System.Net.Mail.MailAddress]::new($SenderMailbox)) {
    throw "SenderMailbox must be a valid email address."
}

$configuredSecrets = @(& $dotnet user-secrets list --project $project 2>$null)
$entraSecretConfigured = @($configuredSecrets | Where-Object { $_ -match '^EntraId:ClientSecret\s*=\s*.+$' }).Count -gt 0
if (-not $entraSecretConfigured) {
    throw "EntraId:ClientSecret is not configured. Run setup-entra-local.ps1 first."
}

& $dotnet user-secrets set "ReportEmail:Enabled" "true" --project $project
if ($LASTEXITCODE -ne 0) { throw "Unable to enable report email." }
& $dotnet user-secrets set "ReportEmail:SenderMailbox" $SenderMailbox --project $project
if ($LASTEXITCODE -ne 0) { throw "Unable to save the sender mailbox." }

Write-Host "Report and user access notification email is enabled for $SenderMailbox." -ForegroundColor Green
Write-Host "Confirm that the Entra app has Microsoft Graph Application permission Mail.Send with admin consent, then restart the system." -ForegroundColor Cyan
