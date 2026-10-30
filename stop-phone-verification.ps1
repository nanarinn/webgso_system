$ErrorActionPreference = 'Stop'
$stateFile = Join-Path $PSScriptRoot '.tools\phone-verification.json'
if (-not (Test-Path -LiteralPath $stateFile)) { Write-Host 'No saved GSO phone tunnel.'; exit }
$state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
$process = Get-CimInstance Win32_Process -Filter "ProcessId=$([int]$state.tunnelPid)" -ErrorAction SilentlyContinue
if ($process -and $process.Name -eq 'cloudflared.exe' -and $process.CommandLine -like "*http://127.0.0.1:$($state.port)*") {
    Stop-Process -Id $process.ProcessId -Force
}
Remove-Item -LiteralPath $stateFile
Write-Host 'Temporary public access stopped. Restart run-java.ps1 normally to restore localhost verification links.'
