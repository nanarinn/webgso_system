param(
    [string]$HostName = 'sblss-lan.local',
    [string]$WebAddress = '192.168.0.173',
    [int]$Port = 8088,
    [string]$SiteName = 'SBLSS',
    [string]$AppPoolName = 'SBLSS'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run setup-iis-web-server.cmd and approve the Windows Administrator prompt.'
}

$dotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
$project = Join-Path $projectRoot 'TestingSystem.Web\TestingSystem.Web.csproj'
if (-not (Test-Path -LiteralPath $dotnet)) { throw '.NET SDK is missing from the project .dotnet folder.' }

$userSecretsId = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.UserSecretsId
if ([string]::IsNullOrWhiteSpace($userSecretsId)) { throw 'The web project has no UserSecretsId.' }
$userSecretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$userSecretsId\secrets.json"
if (-not (Test-Path -LiteralPath $userSecretsPath)) { throw 'The current Windows account has no SBLSS user-secrets file.' }
$secrets = Get-Content -LiteralPath $userSecretsPath -Raw | ConvertFrom-Json
$connectionString = $secrets.ConnectionStrings.DefaultConnection
if ([string]::IsNullOrWhiteSpace($connectionString)) { throw 'The VMware SQL connection string is missing from user secrets.' }
$sqlSettings = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($connectionString)
if ($sqlSettings.DataSource -ne '192.168.227.128,1433' -or
    $sqlSettings.InitialCatalog -ne 'SingleBuyerLssRepository') {
    throw 'The saved SQL connection must target the configured VMware database.'
}
if ($secrets.EntraId.Enabled -ne $true -or [string]::IsNullOrWhiteSpace($secrets.EntraId.ClientSecret)) {
    throw 'Entra ID must remain enabled and its client secret must exist in user secrets.'
}
$pfxPath = Join-Path $projectRoot '.local-certs\sblss-lan.pfx'
if (-not (Test-Path -LiteralPath $pfxPath) -or
    [string]::IsNullOrWhiteSpace($secrets.Kestrel.Certificates.Default.Password)) {
    throw 'The existing LAN PFX certificate or its saved password is missing.'
}

$hostAddress = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
    Where-Object { $_.IPAddress -eq $WebAddress -and $_.InterfaceAlias -notmatch 'Loopback|vEthernet|Docker|WSL' } |
    Select-Object -First 1
if (-not $hostAddress) { throw "The configured web address $WebAddress is not assigned to an active host adapter." }

$vmProbe = [System.Net.Sockets.TcpClient]::new()
try {
    $connect = $vmProbe.BeginConnect('192.168.227.128', 1433, $null, $null)
    if (-not $connect.AsyncWaitHandle.WaitOne(5000)) { throw 'Timed out connecting to the VMware SQL port.' }
    $vmProbe.EndConnect($connect)
}
finally { $vmProbe.Dispose() }

$featureNames = @('IIS-WebServerRole', 'IIS-ManagementConsole', 'IIS-WebSockets')
foreach ($featureName in $featureNames) {
    $feature = Get-WindowsOptionalFeature -Online -FeatureName $featureName
    if ($feature.State -ne 'Enabled') {
        Write-Host "Enabling Windows feature $featureName..." -ForegroundColor Cyan
        $result = Enable-WindowsOptionalFeature -Online -FeatureName $featureName -All -NoRestart
        if ($result.RestartNeeded) { throw 'Windows requested a restart to finish enabling IIS. Restart Windows, then run setup-iis-web-server.cmd again.' }
    }
}

$hostingBundleVersion = '8.0.31'
$hostingBundleUrl = "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/$hostingBundleVersion/dotnet-hosting-$hostingBundleVersion-win.exe"
$hostingBundlePath = Join-Path $env:TEMP "sblss-dotnet-hosting-$hostingBundleVersion-win.exe"
if (-not (Test-Path -LiteralPath $hostingBundlePath)) {
    Write-Host 'Downloading the Microsoft ASP.NET Core IIS Hosting Bundle...' -ForegroundColor Cyan
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $hostingBundleUrl -OutFile $hostingBundlePath -UseBasicParsing
}
$signature = Get-AuthenticodeSignature -FilePath $hostingBundlePath
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw 'The ASP.NET Core Hosting Bundle signature did not validate as Microsoft-signed.'
}
$modulePath = Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
if (-not (Test-Path -LiteralPath $modulePath)) {
    Write-Host 'Installing the signed Microsoft ASP.NET Core Hosting Bundle...' -ForegroundColor Cyan
    $installer = Start-Process -FilePath $hostingBundlePath -ArgumentList @('/install', '/quiet', '/norestart') -Wait -PassThru
    if ($installer.ExitCode -eq 3010) { throw 'Hosting Bundle installed; Windows requires a restart before IIS can host SBLSS. Restart Windows, then run this file again.' }
    if ($installer.ExitCode -ne 0) { throw "Hosting Bundle installer exited with code $($installer.ExitCode)." }
}

Import-Module WebAdministration
Set-Service -Name W3SVC -StartupType Automatic
if ((Get-Service -Name W3SVC).Status -ne 'Running') { Start-Service -Name W3SVC }
$siteRoot = Join-Path $env:SystemDrive "inetpub\wwwroot\$SiteName"
$releaseId = Get-Date -Format 'yyyyMMdd-HHmmss'
$releasePath = Join-Path $siteRoot "releases\$releaseId"
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
Write-Host 'Publishing the SBLSS web application for IIS...' -ForegroundColor Cyan
& $dotnet publish $project -c Release --no-restore -o $releasePath --nologo
if ($LASTEXITCODE -ne 0) { throw 'The Release publish failed; the existing Kestrel site was left running.' }

$pool = Get-Item "IIS:\AppPools\$AppPoolName" -ErrorAction SilentlyContinue
if ($null -eq $pool) { New-WebAppPool -Name $AppPoolName | Out-Null }
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value 'AlwaysRunning'
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name 'processModel.idleTimeout' -Value ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name 'processModel.loadUserProfile' -Value $true

$secureConfigRoot = Join-Path $env:ProgramData 'SBLSS'
New-Item -ItemType Directory -Path $secureConfigRoot -Force | Out-Null
$secureConfigFile = Join-Path $secureConfigRoot 'iis-secrets.json'
Copy-Item -LiteralPath $userSecretsPath -Destination $secureConfigFile -Force
$aclResult = & icacls.exe $secureConfigRoot /inheritance:r /grant:r `
    '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' "IIS AppPool\$AppPoolName`:(OI)(CI)(RX)" /T /C
if ($LASTEXITCODE -ne 0) { throw 'Could not restrict the IIS configuration file permissions.' }

$webConfigPath = Join-Path $releasePath 'web.config'
if (-not (Test-Path -LiteralPath $webConfigPath)) { throw 'Published IIS web.config was not generated.' }
[xml]$webConfig = Get-Content -LiteralPath $webConfigPath -Raw
$aspNetCoreNode = $webConfig.SelectSingleNode('/configuration/system.webServer/aspNetCore')
if ($null -eq $aspNetCoreNode) { $aspNetCoreNode = $webConfig.SelectSingleNode('/configuration/location/system.webServer/aspNetCore') }
if ($null -eq $aspNetCoreNode) { throw 'Published web.config has no ASP.NET Core Module configuration.' }
$envNode = $aspNetCoreNode.SelectSingleNode('environmentVariables')
if ($null -eq $envNode) { $envNode = $webConfig.CreateElement('environmentVariables'); [void]$aspNetCoreNode.AppendChild($envNode) }
foreach ($entry in @(
    @{ Name = 'ASPNETCORE_ENVIRONMENT'; Value = 'Production' },
    @{ Name = 'SBLSS_SECRETS_FILE'; Value = $secureConfigFile }
)) {
    $escapedName = [System.Security.SecurityElement]::Escape($entry.Name)
    $variable = $envNode.SelectSingleNode("environmentVariable[@name='$escapedName']")
    if ($null -eq $variable) { $variable = $webConfig.CreateElement('environmentVariable'); [void]$envNode.AppendChild($variable) }
    $variable.SetAttribute('name', $entry.Name)
    $variable.SetAttribute('value', $entry.Value)
}
$webConfig.Save($webConfigPath)

$siteAcl = & icacls.exe $releasePath /grant "IIS AppPool\$AppPoolName`:(OI)(CI)(RX)" /T /C
if ($LASTEXITCODE -ne 0) { throw 'Could not grant the IIS application pool read access to the published files.' }

$publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
    (Join-Path $projectRoot '.local-certs\sblss-lan.cer'))
$certificate = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Thumbprint -eq $publicCertificate.Thumbprint } | Select-Object -First 1
if ($null -eq $certificate) {
    $pfxPassword = ConvertTo-SecureString $secrets.Kestrel.Certificates.Default.Password -AsPlainText -Force
    $certificate = Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\My -Password $pfxPassword
}
if ($certificate.Thumbprint -ne $publicCertificate.Thumbprint) { throw 'The imported IIS certificate does not match the existing LAN certificate.' }

$otherSiteBindings = foreach ($otherSite in @(Get-Website | Where-Object Name -ne $SiteName)) {
    foreach ($otherBinding in @(Get-WebBinding -Name $otherSite.Name)) {
        $parts = $otherBinding.bindingInformation.Split(':', 3)
        if ($parts.Count -eq 3 -and $parts[1] -eq [string]$Port -and
            ($parts[2] -eq $HostName -or [string]::IsNullOrEmpty($parts[2]))) {
            [pscustomobject]@{ Site = $otherSite.Name; Binding = $otherBinding.bindingInformation }
        }
    }
}
if ($otherSiteBindings) { throw "IIS site '$($otherSiteBindings[0].Site)' already owns the requested host/port binding. No other site's binding was changed." }

# Stop only the development watcher/application launched from this repository;
# refuse to kill an unrelated process that happens to own the website port.
$projectPattern = [regex]::Escape($projectRoot)
$watchers = @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
    Where-Object { $_.CommandLine -match '(?i)\bwatch\b' -and $_.CommandLine -match $projectPattern })
foreach ($watcher in $watchers) { Stop-Process -Id $watcher.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Milliseconds 500
$listeners = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique)
foreach ($processId in $listeners) {
    if ($processId -eq 4) { continue } # HTTP.sys may be serving unrelated SNI bindings on the same port.
    $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction SilentlyContinue
    $isSblss = $processInfo -and (
        $processInfo.ExecutablePath -like "$projectRoot*" -or $processInfo.CommandLine -match $projectPattern)
    if (-not $isSblss) { throw "Port $Port is held by another process (PID $processId); it was left running." }
    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
}

$existingSite = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
if ($null -ne $existingSite) {
    Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
    foreach ($binding in @(Get-WebBinding -Name $SiteName)) {
        $parts = $binding.bindingInformation.Split(':', 3)
        if ($parts.Count -eq 3 -and $parts[1] -eq [string]$Port -and
            ($parts[2] -eq $HostName -or [string]::IsNullOrEmpty($parts[2]))) {
            Remove-WebBinding -Name $SiteName -Protocol $binding.protocol -Port $Port -HostHeader $parts[2]
        }
    }
}
if ($null -eq $existingSite) {
    New-Website -Name $SiteName -PhysicalPath $releasePath -ApplicationPool $AppPoolName `
        -IPAddress '*' -Port $Port -HostHeader $HostName | Out-Null
}
else {
    Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $releasePath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
}

foreach ($binding in @(Get-WebBinding -Name $SiteName)) {
    $parts = $binding.bindingInformation.Split(':', 3)
    if ($parts.Count -eq 3 -and $parts[1] -eq [string]$Port -and
        ($parts[2] -eq $HostName -or [string]::IsNullOrEmpty($parts[2]))) {
        Remove-WebBinding -Name $SiteName -Protocol $binding.protocol -Port $Port -HostHeader $parts[2]
    }
}
New-WebBinding -Name $SiteName -Protocol https -IPAddress '*' -Port $Port -HostHeader $HostName -SslFlags 1
$httpsBinding = Get-WebBinding -Name $SiteName -Protocol https -Port $Port -HostHeader $HostName
$httpsBinding.AddSslCertificate($certificate.Thumbprint, 'My')
Set-ItemProperty "IIS:\Sites\$SiteName" -Name serverAutoStart -Value $true

$ruleName = 'SBLSS IIS HTTPS 8088 (LocalSubnet)'
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP `
        -LocalPort $Port -RemoteAddress LocalSubnet -Profile Any | Out-Null
}
else { Set-NetFirewallRule -DisplayName $ruleName -Enabled True -Profile Any | Out-Null }

Start-WebAppPool -Name $AppPoolName
Start-Website -Name $SiteName
Write-Host 'Waiting for the IIS site and VMware database health check...' -ForegroundColor Cyan
$previousCallback = [Net.ServicePointManager]::ServerCertificateValidationCallback
[Net.ServicePointManager]::ServerCertificateValidationCallback = { param($sender, $cert, $chain, $errors) return $true }
try {
    $healthy = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri "https://$HostName`:$Port/health" -UseBasicParsing -TimeoutSec 4
            if ([int]$response.StatusCode -eq 200 -and $response.Content -match 'Healthy') { $healthy = $true; break }
        }
        catch { Start-Sleep -Seconds 2 }
    }
}
finally { [Net.ServicePointManager]::ServerCertificateValidationCallback = $previousCallback }
if (-not $healthy) { throw "IIS is configured, but /health did not become healthy. Inspect Event Viewer > Windows Logs > Application. The existing site URL is https://$HostName`:$Port/login." }

Write-Host 'IIS web server is ready.' -ForegroundColor Green
Write-Host "Website: https://$HostName`:$Port/login" -ForegroundColor Green
Write-Host 'Database: VMware SQL Server (192.168.227.128:1433 / SingleBuyerLssRepository)' -ForegroundColor Green
Write-Host 'Authentication: Microsoft Entra ID' -ForegroundColor Green
