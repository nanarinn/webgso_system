param([int]$Port = 5187, [string]$CloudflaredPath)
$ErrorActionPreference = 'Stop'
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Choose an application port between 1024 and 65535.' }
$projectRoot = $PSScriptRoot
$stateRoot = Join-Path $projectRoot '.tools'
New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
if (-not $CloudflaredPath) {
    $portable = Join-Path (Split-Path $projectRoot -Parent) '.tools\cloudflared.exe'
    $CloudflaredPath = if (Test-Path -LiteralPath $portable) { $portable } else { (Get-Command cloudflared -ErrorAction Stop).Source }
}
if (-not (Test-Path -LiteralPath $CloudflaredPath)) { throw 'Install official cloudflared first. See docs/PHONE_VERIFICATION.md.' }

$stateFile = Join-Path $stateRoot 'phone-verification.json'
if (Test-Path -LiteralPath $stateFile) {
    $prior = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
    $priorProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$([int]$prior.tunnelPid)" -ErrorAction SilentlyContinue
    if ($priorProcess -and $priorProcess.ExecutablePath -eq (Resolve-Path -LiteralPath $CloudflaredPath).Path) {
        throw "A GSO phone tunnel is already running: $($prior.url). Use its existing link."
    }
}
$tunnelLog = Join-Path $stateRoot 'phone-tunnel.log'
$tunnelOut = Join-Path $stateRoot 'phone-tunnel-stdout.log'
$tunnel = Start-Process -FilePath $CloudflaredPath -ArgumentList @('tunnel','--url',"http://127.0.0.1:$Port",'--protocol','http2','--no-autoupdate') -WindowStyle Hidden -PassThru -RedirectStandardOutput $tunnelOut -RedirectStandardError $tunnelLog
try {
    $url = $null
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        if ($tunnel.HasExited) { throw 'The HTTPS tunnel exited. Check .tools/phone-tunnel.log.' }
        $log = if (Test-Path -LiteralPath $tunnelLog) { [string](Get-Content -LiteralPath $tunnelLog -Raw) } else { '' }
        $match = [regex]::Match($log, 'https://[a-z0-9-]+\.trycloudflare\.com')
        if ($match.Success) { $url = $match.Value; break }
        Start-Sleep -Seconds 1
    }
    if (-not $url) { throw 'No temporary HTTPS URL was returned. Check .tools/phone-tunnel.log.' }
    # Restart only this project's Java process on the requested port.
    $jar = Join-Path $projectRoot 'target\webgso-system-2.0.0-SNAPSHOT.jar'
    Get-CimInstance Win32_Process -Filter "name='java.exe'" | Where-Object {
        $_.CommandLine -like "*$jar*" -and $_.CommandLine -match "--server\.port=$Port(?:\s|$)"
    } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    $runner = Join-Path $projectRoot 'run-java.ps1'
    $application = Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $runner + '"'),'-Port',"$Port",'-PublicBaseUrl',$url) -WindowStyle Hidden -WorkingDirectory $projectRoot -PassThru -RedirectStandardOutput (Join-Path $stateRoot 'phone-app.log') -RedirectStandardError (Join-Path $stateRoot 'phone-app-error.log')
    @{url=$url;port=$Port;tunnelPid=$tunnel.Id;runnerPid=$application.Id;startedAt=[DateTimeOffset]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
    Write-Host "Temporary GSO HTTPS address: $url" -ForegroundColor Green
    Write-Host "Sign up: $url/register"
    Write-Host 'Keep this computer, the application and cloudflared running. New verification emails use this address.'
    Write-Host 'To stop the tunnel, run stop-phone-verification.ps1.'
} catch {
    if (-not $tunnel.HasExited) { Stop-Process -Id $tunnel.Id -Force }
    throw
}
