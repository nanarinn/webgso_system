$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot "TestingSystem.Web\TestingSystem.Web.csproj"
$settingsFile = Join-Path $projectRoot "TestingSystem.Web\appsettings.json"

Set-Location -LiteralPath $projectRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $dotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
}
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw ".NET 8 SDK was not found. Install the .NET 8 SDK, then run this script again."
}

$settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
$expectedTenantId = "27971c0d-2474-4fa6-bc9c-d30415822fc9"
$expectedClientId = "a596ced9-3a3e-4022-be33-99df22d4e569"

if ($settings.EntraId.TenantId -ne $expectedTenantId) {
    throw "The configured Entra tenant ID does not match the reviewed SAE Malaysia tenant."
}
if ($settings.EntraId.ClientId -ne $expectedClientId) {
    throw "The configured Entra client ID does not match the reviewed sblss_system app registration."
}

Write-Host "Trusting the ASP.NET Core HTTPS development certificate..." -ForegroundColor Cyan
& $dotnet dev-certs https --trust
if ($LASTEXITCODE -ne 0) {
    throw "The HTTPS certificate was not trusted. Approve the Windows certificate prompt and run this script again."
}

$secureSecret = Read-Host "Paste the sblss_system Client Secret VALUE" -AsSecureString
$secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSecret)
try {
    $clientSecret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer)
    if ([string]::IsNullOrWhiteSpace($clientSecret)) {
        throw "Client Secret Value cannot be empty."
    }

    $parsedSecretId = [Guid]::Empty
    if ([Guid]::TryParse($clientSecret, [ref]$parsedSecretId)) {
        throw "This looks like a Secret ID. Copy the secret VALUE from Certificates & secrets instead."
    }

    Write-Host "Validating the Client Secret Value with Microsoft Entra..." -ForegroundColor Cyan
    try {
        $tokenEndpoint = "https://login.microsoftonline.com/$expectedTenantId/oauth2/v2.0/token"
        $tokenResponse = Invoke-RestMethod -Method Post -Uri $tokenEndpoint -ContentType "application/x-www-form-urlencoded" -Body @{
            client_id     = $expectedClientId
            client_secret = $clientSecret
            scope         = "https://graph.microsoft.com/.default"
            grant_type    = "client_credentials"
        }
        if ([string]::IsNullOrWhiteSpace($tokenResponse.access_token)) {
            throw "Microsoft Entra did not return an access token."
        }
    }
    catch {
        $entraMessage = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($entraMessage)) {
            $entraMessage = $_.Exception.Message
        }
        if ($entraMessage -match "AADSTS7000215") {
            throw "Microsoft rejected this secret. Use the secret VALUE from the sblss_system App Registration, not its Secret ID."
        }
        if ($entraMessage -match "AADSTS7000222") {
            throw "This Client Secret has expired. Create a new secret in the sblss_system App Registration."
        }
        throw "Unable to validate the Entra Client Secret: $entraMessage"
    }

    Write-Host "Client Secret Value is valid for the configured sblss_system application." -ForegroundColor Green

    & $dotnet user-secrets set "EntraId:ClientSecret" $clientSecret --project $projectFile
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to save the Entra client secret."
    }
}
finally {
    if ($secretPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer)
    }
    $clientSecret = $null
    $secureSecret = $null
}

& $dotnet user-secrets set "EntraId:Enabled" "true" --project $projectFile
if ($LASTEXITCODE -ne 0) {
    throw "Unable to enable Microsoft Entra sign-in."
}

& $dotnet dev-certs https --check --trust --quiet
if ($LASTEXITCODE -ne 0) {
    throw "The HTTPS certificate is still not trusted."
}

Write-Host "Microsoft Entra local configuration is ready." -ForegroundColor Green
Write-Host "Starting SBLSS at https://localhost:8088/login" -ForegroundColor Green
& (Join-Path $projectRoot "run-local-sqlserver.ps1")
