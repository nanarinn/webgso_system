param(
    [string]$IPAddress = "192.168.0.140",
    [string]$HostName = "sblss-lan.local"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot "TestingSystem.Web\TestingSystem.Web.csproj"
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $dotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
}
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw '.NET 8 SDK was not found. Install the .NET 8 SDK, then run this script again.'
}
$certificateDirectory = Join-Path $projectRoot ".local-certs"
$pfxPath = Join-Path $certificateDirectory "sblss-lan.pfx"
$publicCertificatePath = Join-Path $certificateDirectory "sblss-lan.cer"
$hostsPath = Join-Path $env:WINDIR "System32\drivers\etc\hosts"
$firewallRuleName = "SBLSS Entra HTTPS Local Network"

$isAdministrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdministrator) {
    throw "Run this script from an Administrator PowerShell or VS Code terminal."
}

$parsedAddress = [System.Net.IPAddress]::None
if (-not [System.Net.IPAddress]::TryParse($IPAddress, [ref]$parsedAddress) -or
    $parsedAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "$IPAddress is not a valid IPv4 address."
}
$assignedAddress = Get-NetIPAddress -AddressFamily IPv4 -IPAddress $IPAddress -ErrorAction SilentlyContinue
if ($null -eq $assignedAddress) {
    throw "$IPAddress is not assigned to this computer. Configure the host IP before running LAN Entra setup."
}
if ($HostName -notmatch '^[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?$') {
    throw "$HostName is not a valid local hostname."
}

New-Item -ItemType Directory -Path $certificateDirectory -Force | Out-Null
$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.FriendlyName -eq "SBLSS LAN HTTPS" -and $_.NotAfter -gt (Get-Date).AddDays(30) -and
    $_.DnsNameList.Unicode -contains $HostName
} | Sort-Object NotAfter -Descending | Select-Object -First 1

if ($null -eq $certificate) {
    Write-Host "Creating an HTTPS certificate for $HostName..." -ForegroundColor Cyan
    $certificate = New-SelfSignedCertificate -Type SSLServerAuthentication -Subject "CN=$HostName" `
        -DnsName $HostName -FriendlyName "SBLSS LAN HTTPS" -CertStoreLocation "Cert:\CurrentUser\My" `
        -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears(2)
}

$passwordBytes = New-Object byte[] 32
$randomGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $randomGenerator.GetBytes($passwordBytes)
}
finally {
    $randomGenerator.Dispose()
}
$pfxPasswordText = [Convert]::ToBase64String($passwordBytes)
$pfxPassword = ConvertTo-SecureString $pfxPasswordText -AsPlainText -Force
Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $pfxPassword -Force | Out-Null
Export-Certificate -Cert $certificate -FilePath $publicCertificatePath -Force | Out-Null
Import-Certificate -FilePath $publicCertificatePath -CertStoreLocation "Cert:\CurrentUser\Root" | Out-Null

$hostsContent = Get-Content -LiteralPath $hostsPath -ErrorAction Stop
$hostsContent = @($hostsContent | Where-Object { $_ -notmatch "\s$([regex]::Escape($HostName))(?:\s|$)" })
$hostsContent += "$IPAddress`t$HostName`t# SBLSS LAN"
Set-Content -LiteralPath $hostsPath -Value $hostsContent -Encoding ascii

& $dotnet user-secrets set "Kestrel:Certificates:Default:Path" $pfxPath --project $projectFile | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not save the LAN certificate path.' }
& $dotnet user-secrets set "Kestrel:Certificates:Default:Password" $pfxPasswordText --project $projectFile | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not save the LAN certificate password.' }
& $dotnet user-secrets set "EntraId:Enabled" "true" --project $projectFile | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not enable Microsoft Entra sign-in.' }

$existingRule = Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue
if ($null -eq $existingRule) {
    New-NetFirewallRule -DisplayName $firewallRuleName -Direction Inbound -Action Allow -Protocol TCP `
        -LocalPort 8088 -RemoteAddress LocalSubnet -Profile Any | Out-Null
}
else {
    Set-NetFirewallRule -DisplayName $firewallRuleName -Enabled True -Profile Any | Out-Null
    $existingRule | Get-NetFirewallAddressFilter | Set-NetFirewallAddressFilter -RemoteAddress LocalSubnet | Out-Null
}

Write-Host "LAN HTTPS setup is ready." -ForegroundColor Green
Write-Host "Add this Web Redirect URI to the sblss_system App Registration:" -ForegroundColor Yellow
Write-Host "https://$HostName`:8088/signin-oidc" -ForegroundColor White
Write-Host "Give teammates these two files:" -ForegroundColor Cyan
Write-Host "  $publicCertificatePath"
Write-Host "  $(Join-Path $projectRoot 'trust-sblss-lan-certificate.ps1')"
Write-Host "After the Redirect URI is saved, start with:" -ForegroundColor Cyan
Write-Host "powershell -ExecutionPolicy Bypass -File .\run-local-sqlserver.ps1 -VmSqlServer -Share -ShareAddress $IPAddress -ShareEntra -ShareHostName $HostName"
