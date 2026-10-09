param(
    [string]$IPAddress = "10.122.147.3",
    [string]$HostName = "sblss-lan.local",
    [string]$CertificatePath = ".\sblss-lan.cer"
)

$ErrorActionPreference = "Stop"
$isAdministrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdministrator) {
    throw "Run this script from an Administrator PowerShell terminal."
}
$resolvedCertificate = Resolve-Path -LiteralPath $CertificatePath -ErrorAction Stop
$certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2($resolvedCertificate.Path)
if ($certificate.Subject -ne "CN=$HostName") {
    throw "This certificate does not belong to $HostName."
}

Import-Certificate -FilePath $resolvedCertificate.Path -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
$hostsPath = Join-Path $env:WINDIR "System32\drivers\etc\hosts"
$hostsContent = Get-Content -LiteralPath $hostsPath -ErrorAction Stop
$hostsContent = @($hostsContent | Where-Object { $_ -notmatch "\s$([regex]::Escape($HostName))(?:\s|$)" })
$hostsContent += "$IPAddress`t$HostName`t# SBLSS ZeroTier"
Set-Content -LiteralPath $hostsPath -Value $hostsContent -Encoding ascii

Write-Host "SBLSS ZeroTier certificate and hostname are trusted." -ForegroundColor Green
Write-Host "Open https://$HostName`:8088/login" -ForegroundColor Cyan

