param()

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

$userName = (Read-Host "Super Admin username").Trim()
$email = (Read-Host "Super Admin email").Trim()
$displayName = (Read-Host "Super Admin display name").Trim()
if ([string]::IsNullOrWhiteSpace($userName) -or [string]::IsNullOrWhiteSpace($email) -or [string]::IsNullOrWhiteSpace($displayName)) {
    throw "Username, email and display name are required."
}

$securePassword = Read-Host "Super Admin password (minimum 12 characters, uppercase, lowercase, number and symbol)" -AsSecureString
$secureConfirmation = Read-Host "Confirm Super Admin password" -AsSecureString

function Convert-SecureValue([Security.SecureString]$value) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($value)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$password = Convert-SecureValue $securePassword
$confirmation = Convert-SecureValue $secureConfirmation
if ($password -cne $confirmation) { throw "The passwords do not match." }

$env:WEBGSO_BOOTSTRAP_PASSWORD = $password
$env:UiOnly__Enabled = "false"
$env:ASPNETCORE_ENVIRONMENT = "Development"
try {
    $bootstrapOutput = Join-Path $projectRoot ".bootstrap-build"
    Write-Host "Building the local account setup utility..." -ForegroundColor Cyan
    & dotnet build .\TestingSystem.Web\TestingSystem.Web.csproj --no-restore --output $bootstrapOutput
    if ($LASTEXITCODE -ne 0) { throw "Unable to build the local account setup utility." }

    & dotnet (Join-Path $bootstrapOutput "TestingSystem.Web.dll") `
        --create-local-admin $userName $email $displayName
    if ($LASTEXITCODE -ne 0) { throw "Unable to create the local Super Admin account." }
}
finally {
    Remove-Item Env:WEBGSO_BOOTSTRAP_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:UiOnly__Enabled -ErrorAction SilentlyContinue
    Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
    $password = $null
    $confirmation = $null
}

Write-Host "Local Super Admin setup completed." -ForegroundColor Green
Write-Host "Sign in with username '$userName' at http://localhost:5186/login" -ForegroundColor Cyan
