$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$watchScript = Join-Path $repoRoot 'watch-design-aws.ps1'
$stateRoot = Join-Path $repoRoot '.tools/design-sync'
New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
$pidPath = Join-Path $stateRoot 'watch.pid'
if (Test-Path -LiteralPath $pidPath) {
    $watchId = [int](Get-Content -LiteralPath $pidPath -Raw).Trim()
    $process = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $watchId) -ErrorAction SilentlyContinue
    if ($process -and $process.CommandLine -like ('*' + $watchScript + '*')) {
        Write-Host 'Auto-sync is already running.'
        exit 0
    }
}
$arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $watchScript + '"'
$child = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stateRoot 'watch.out.log') -RedirectStandardError (Join-Path $stateRoot 'watch.err.log')
Start-Sleep -Seconds 2
if ($child.HasExited) { throw 'Auto-sync failed to start. Check .tools/design-sync/watch.err.log.' }
Write-Host 'Auto-sync started: saved coding/design changes deploy through Systems Manager after tests pass. No SSH.'
Write-Host ('Status: ' + (Join-Path $stateRoot 'status.log'))
