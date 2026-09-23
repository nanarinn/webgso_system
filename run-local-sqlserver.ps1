param(
    [switch]$Pull,
    [switch]$Share,
    [string]$ShareAddress,
    [switch]$ShareEntra,
    [string]$ShareHostName = "sblss-lan.local"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

if ($Pull) {
    $pendingChanges = @(git status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the Git working tree."
    }
    if ($pendingChanges.Count -gt 0) {
        throw "Git pull was cancelled because local changes exist. Commit or stash them first, then run this command again."
    }

    Write-Host "Pulling the latest system code from origin/main..." -ForegroundColor Cyan
    git -c core.pager=cat pull --ff-only origin main
    if ($LASTEXITCODE -ne 0) {
        throw "Git pull failed. Review the Git authentication or branch message shown above."
    }
}

$sqlService = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction Stop
if ($sqlService.Status -ne 'Running') {
    Start-Service -Name 'MSSQL$SQLEXPRESS'
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$port = 5186
$httpsPort = 8088
$httpsReady = $false
dotnet dev-certs https --check --trust --quiet
if ($LASTEXITCODE -eq 0) {
    $httpsReady = $true
}

$configuredSecrets = @(dotnet user-secrets list --project .\TestingSystem.Web\TestingSystem.Web.csproj 2>$null)
$appSettings = Get-Content -LiteralPath (Join-Path $projectRoot "TestingSystem.Web\appsettings.json") -Raw | ConvertFrom-Json
$entraEnabled = $appSettings.EntraId.Enabled -eq $true -or @($configuredSecrets | Where-Object { $_ -match '^EntraId:Enabled\s*=\s*true$' }).Count -gt 0
$entraSecretConfigured = @($configuredSecrets | Where-Object { $_ -match '^EntraId:ClientSecret\s*=\s*.+$' }).Count -gt 0
$lanCertificatePathConfigured = @($configuredSecrets | Where-Object { $_ -match '^Kestrel:Certificates:Default:Path\s*=\s*.+$' }).Count -gt 0
$lanCertificatePasswordConfigured = @($configuredSecrets | Where-Object { $_ -match '^Kestrel:Certificates:Default:Password\s*=\s*.+$' }).Count -gt 0
$lanCertificateConfigured = $lanCertificatePathConfigured -and $lanCertificatePasswordConfigured
if ($ShareEntra) {
    if (-not $Share) {
        throw "ShareEntra must be used together with -Share."
    }
    if (-not $lanCertificateConfigured) {
        throw "LAN HTTPS is not configured. Run .\setup-lan-entra.ps1 first."
    }
    $httpsReady = $true
}
$entraReady = $httpsReady -and $entraEnabled -and $entraSecretConfigured

# A previous dotnet watch process can survive after its web child is stopped and
# immediately start another SBLSS instance. Remove only watchers that target this
# project before synchronising the database and opening the ports again.
$staleWatchers = @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object {
        $_.CommandLine -match '(?i)\bwatch\b' -and
        $_.CommandLine -match '(?i)TestingSystem\.Web'
    })
if ($staleWatchers.Count -gt 0) {
    Write-Host "Stopping $($staleWatchers.Count) previous SBLSS development watcher(s)..." -ForegroundColor Yellow
    foreach ($watcher in $staleWatchers) {
        Stop-Process -Id $watcher.ProcessId -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 700
}

$runningInstances = @(Get-Process -Name "TestingSystem.Web" -ErrorAction SilentlyContinue)
if ($runningInstances.Count -gt 0) {
    Write-Host "Stopping previous SBLSS instance..." -ForegroundColor Yellow
    $runningInstances | Stop-Process -Force
    $runningInstances | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

Write-Host "Synchronising the Microsoft SQL Server schema with the current system..." -ForegroundColor Cyan
dotnet run --project .\TestingSystem.Web\TestingSystem.Web.csproj --no-launch-profile -- --update-database
if ($LASTEXITCODE -ne 0) {
    throw "Database schema synchronisation failed. The system was not started."
}

$availableNetworkInterfaces = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object {
        $_.IPAddress -ne '127.0.0.1' -and
        $_.IPAddress -notlike '169.254.*' -and
        $_.InterfaceAlias -notmatch 'Loopback|vEthernet|Docker|WSL'
    })
$networkInterface = if ([string]::IsNullOrWhiteSpace($ShareAddress)) {
    $availableNetworkInterfaces | Sort-Object -Property InterfaceMetric | Select-Object -First 1
}
else {
    $parsedShareAddress = [System.Net.IPAddress]::None
    if (-not [System.Net.IPAddress]::TryParse($ShareAddress, [ref]$parsedShareAddress) -or
        $parsedShareAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        throw "ShareAddress must be a valid IPv4 address."
    }
    $availableNetworkInterfaces | Where-Object { $_.IPAddress -eq $ShareAddress } | Select-Object -First 1
}
$networkAddress = $networkInterface.IPAddress

if ($Share) {
    if ([string]::IsNullOrWhiteSpace($networkAddress)) {
        if ([string]::IsNullOrWhiteSpace($ShareAddress)) {
            throw "No active LAN or Wi-Fi IPv4 address was found for network sharing."
        }
        throw "The address $ShareAddress is not currently assigned to an active LAN or Wi-Fi adapter on this computer."
    }
    $isAdministrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdministrator) {
        throw "Network sharing needs an Administrator terminal to create the LocalSubnet firewall rule."
    }
    $networkProfile = Get-NetConnectionProfile -InterfaceIndex $networkInterface.InterfaceIndex -ErrorAction SilentlyContinue
    $firewallProfile = if ($null -eq $networkProfile) {
        "Any"
    }
    elseif ($networkProfile.NetworkCategory -eq 'DomainAuthenticated') {
        "Domain"
    }
    else {
        $networkProfile.NetworkCategory.ToString()
    }
    [string[]]$sharedPorts = if ($ShareEntra) { @("$port", "$httpsPort") } else { @("$port") }
    $sharedPortsLabel = $sharedPorts -join ", "
    if ($firewallProfile -eq 'Public') {
        Write-Host "The active network is Public. Access will still be limited to LocalSubnet on TCP port(s) $sharedPortsLabel." -ForegroundColor Yellow
    }

    $firewallRuleName = "SBLSS Local Network Development"
    $existingRule = Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue
    if ($null -eq $existingRule) {
        New-NetFirewallRule -DisplayName $firewallRuleName -Direction Inbound -Action Allow `
            -Protocol TCP -LocalPort $sharedPorts -RemoteAddress LocalSubnet -Profile $firewallProfile | Out-Null
        Write-Host "Created a $firewallProfile-network firewall rule for TCP port(s) $sharedPortsLabel (LocalSubnet only)." -ForegroundColor Green
    }
    else {
        Set-NetFirewallRule -DisplayName $firewallRuleName -Enabled True -Profile $firewallProfile | Out-Null
        $existingRule | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -LocalPort $sharedPorts | Out-Null
        $existingRule | Get-NetFirewallAddressFilter | Set-NetFirewallAddressFilter -RemoteAddress LocalSubnet | Out-Null
        Write-Host "Local network firewall rule is ready." -ForegroundColor Green
    }
}

Write-Host "Local access:   http://localhost:$port" -ForegroundColor Cyan
if ($httpsReady) {
    if ($ShareEntra) {
        Write-Host "Secure access:  https://$ShareHostName`:$httpsPort" -ForegroundColor Green
    }
    else {
        Write-Host "Secure access:  https://localhost:$httpsPort" -ForegroundColor Green
    }
}
else {
    Write-Host "HTTPS certificate is not installed or trusted. Local login remains available." -ForegroundColor Yellow
    Write-Host "To enable Microsoft sign-in, stop the system and run: dotnet dev-certs https --trust" -ForegroundColor Yellow
}
if ($entraReady) {
    $entraLoginHost = if ($ShareEntra) { $ShareHostName } else { "localhost" }
    Write-Host "Microsoft sign-in: ready at https://$entraLoginHost`:$httpsPort/login" -ForegroundColor Green
}
elseif ($httpsReady) {
    Write-Host "Microsoft sign-in is pending EntraId:Enabled and EntraId:ClientSecret user secrets." -ForegroundColor Yellow
    Write-Host "See the Microsoft Entra ID sign-in section in README.md." -ForegroundColor Yellow
}
if ($networkAddress) {
    if ($Share) {
        Write-Host "Team network access: http://$($networkAddress):$port" -ForegroundColor Green
        if ($ShareEntra) {
            Write-Host "Team Microsoft Entra access: https://$ShareHostName`:$httpsPort/login" -ForegroundColor Green
        }
        Write-Host "Keep this terminal running while teammates use the system." -ForegroundColor DarkYellow
    }
    else {
        Write-Host "Network URL detected: http://$($networkAddress):$port" -ForegroundColor Cyan
        Write-Host "Run this script with -Share in an Administrator terminal to allow teammate access." -ForegroundColor Yellow
    }
}

$env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = "1"
$env:DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER = "1"
Write-Host "Development watch is active. Saving a code file will rebuild and restart the system automatically." -ForegroundColor Green
Write-Host "Open the system with http://localhost:$port (do not use 0.0.0.0 in the browser)." -ForegroundColor Cyan
Write-Host "Press Ctrl + C only when you want to stop the system." -ForegroundColor DarkYellow
$applicationUrls = if ($httpsReady) {
    $httpsBindHost = if ($ShareEntra) { "0.0.0.0" } else { "localhost" }
    "https://$httpsBindHost`:$httpsPort;http://0.0.0.0:$port"
}
else {
    "http://0.0.0.0:$port"
}
$watchLogPath = Join-Path $projectRoot ".sblss-watch.log"
dotnet watch --project .\TestingSystem.Web\TestingSystem.Web.csproj --no-hot-reload run --no-launch-profile --urls `
    $applicationUrls 2>&1 | Tee-Object -FilePath $watchLogPath
$watchExitCode = $LASTEXITCODE
if ($watchExitCode -ne 0) {
    throw "SBLSS stopped with exit code $watchExitCode. Startup details were saved to $watchLogPath"
}
